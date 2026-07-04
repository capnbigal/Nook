using Microsoft.EntityFrameworkCore;
using Nook.Data;
using Nook.Models;

namespace Nook.Services;

/// <summary>
/// Migrates the legacy <see cref="Item"/> model into the graph model. All steps
/// are idempotent (guarded by NOT-EXISTS checks) so the whole process can be
/// re-run safely. Node primary keys are preserved (NodeId == ItemId) so every
/// legacy reference maps by identity with no crosswalk.
/// </summary>
public sealed class GraphMigrationService : IGraphMigrationService
{
    private readonly IDbContextFactory<NookContext> _factory;

    public GraphMigrationService(IDbContextFactory<NookContext> factory) => _factory = factory;

    // ---- Kind / status / priority mapping (documented, deterministic) ----

    private static NodeKind MapKind(ItemType t) => t switch
    {
        ItemType.Note => NodeKind.Note,
        ItemType.Thought => NodeKind.Note,     // no Thought kind; nearest record kind
        ItemType.Bookmark => NodeKind.Bookmark,
        ItemType.Idea => NodeKind.Idea,
        ItemType.Reference => NodeKind.Reference,
        ItemType.List => NodeKind.List,
        ItemType.Todo => NodeKind.Note,        // locked: Todo -> Node(Note) + Action
        ItemType.Reminder => NodeKind.Note,    // locked: Reminder -> Node(Note) + Action
        _ => NodeKind.Unclassified,
    };

    private static ActionStatus MapStatus(ItemStatus s) => s switch
    {
        ItemStatus.Open => ActionStatus.Open,
        ItemStatus.InProgress => ActionStatus.InProgress,
        ItemStatus.Done => ActionStatus.Done,
        ItemStatus.Cancelled => ActionStatus.Cancelled,
        _ => ActionStatus.Open,
    };

    private static ActionPriority? MapPriority(Priority? p) => p switch
    {
        Priority.Low => ActionPriority.Low,
        Priority.Medium => ActionPriority.Medium,
        Priority.High => ActionPriority.High,
        Priority.Urgent => ActionPriority.Urgent,
        _ => null,
    };

    // ---- Seed ----

    public async Task<int> SeedSystemDataAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        int added = 0;

        var existingRel = await db.RelationTypes.Where(rt => rt.UserId == null)
            .Select(rt => rt.Name).ToListAsync();
        var relSet = existingRel.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var seed in GraphSeedData.RelationTypes)
        {
            if (relSet.Contains(seed.Name)) continue;
            db.RelationTypes.Add(new RelationType
            {
                Name = seed.Name,
                InverseName = seed.InverseName,
                IsSymmetric = seed.IsSymmetric,
                Category = seed.Category,
                IsSystem = true,
                UserId = null,
            });
            added++;
        }

        var existingVerb = await db.Verbs.Where(v => v.UserId == null)
            .Select(v => v.Name).ToListAsync();
        var verbSet = existingVerb.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var name in GraphSeedData.Verbs)
        {
            if (verbSet.Contains(name)) continue;
            db.Verbs.Add(new Verb { Name = name, IsSystem = true, UserId = null });
            added++;
        }

        await db.SaveChangesAsync();
        return added;
    }

    // ---- Backfill ----

    public async Task<BackfillResult> BackfillAsync()
    {
        await SeedSystemDataAsync();

        var nodesCreated = await BackfillNodesAsync();
        await using var db = await _factory.CreateDbContextAsync();

        // Load reference maps once.
        var relTypes = await db.RelationTypes.Where(rt => rt.UserId == null).ToListAsync();
        var relByName = relTypes.ToDictionary(rt => rt.Name, StringComparer.OrdinalIgnoreCase);
        var relatedTo = relByName[GraphSeedData.DefaultRelationTypeName];
        var contains = relByName[GraphSeedData.ContainsRelationTypeName];

        var items = await db.Items.AsNoTracking().ToListAsync();
        var itemById = items.ToDictionary(i => i.ItemId);

        // ---- NodeTags from ItemTags ----
        var existingNodeTags = (await db.NodeTags.Select(nt => new { nt.NodeId, nt.TagId }).ToListAsync())
            .Select(x => (x.NodeId, x.TagId)).ToHashSet();
        var nodeIds = (await db.Nodes.Select(n => n.NodeId).ToListAsync()).ToHashSet();
        int nodeTagsCreated = 0;
        foreach (var it in await db.ItemTags.AsNoTracking().ToListAsync())
        {
            if (!nodeIds.Contains(it.ItemId)) continue;
            if (existingNodeTags.Contains((it.ItemId, it.TagId))) continue;
            db.NodeTags.Add(new NodeTag { NodeId = it.ItemId, TagId = it.TagId });
            existingNodeTags.Add((it.ItemId, it.TagId));
            nodeTagsCreated++;
        }

        // ---- NodeRelations from ItemLinks ----
        var existingRels = (await db.NodeRelations
            .Select(r => new { r.UserId, r.SourceNodeId, r.TargetNodeId, r.RelationTypeId }).ToListAsync())
            .Select(x => (x.UserId, x.SourceNodeId, x.TargetNodeId, x.RelationTypeId)).ToHashSet();
        int relationsCreated = 0, unmapped = 0;
        foreach (var link in await db.ItemLinks.AsNoTracking().ToListAsync())
        {
            if (!itemById.TryGetValue(link.SourceItemId, out var src)) continue;
            if (!nodeIds.Contains(link.SourceItemId) || !nodeIds.Contains(link.TargetItemId)) continue;

            var rt = relatedTo;
            if (!string.IsNullOrWhiteSpace(link.LinkType))
            {
                if (relByName.TryGetValue(link.LinkType.Trim(), out var matched))
                {
                    rt = matched;
                }
                else
                {
                    unmapped++;
                    await LogAuditAsync(db, "UnmappedLinkLabel", link.SourceItemId,
                        $"Link label '{link.LinkType}' ({link.SourceItemId}->{link.TargetItemId}) mapped to 'related to'.");
                }
            }

            var (s, t) = Canonicalize(rt, link.SourceItemId, link.TargetItemId);
            if (AddRelationIfNew(db, existingRels, src.UserId, s, t, rt.RelationTypeId, link.CreatedAt, null))
                relationsCreated++;
        }

        // ---- contains relations from ParentItemId ----
        int containsCreated = 0;
        foreach (var item in items.Where(i => i.ParentItemId is not null))
        {
            var parent = item.ParentItemId!.Value;
            if (!nodeIds.Contains(parent) || !nodeIds.Contains(item.ItemId)) continue;
            if (AddRelationIfNew(db, existingRels, item.UserId, parent, item.ItemId,
                    contains.RelationTypeId, item.CreatedAt, null))
                containsCreated++;
        }

        // ---- Actions from Todos / Reminders ----
        var existingActions = (await db.ActionItems
            .Select(a => new { a.UserId, a.TargetNodeId, a.Kind, a.Title }).ToListAsync())
            .Select(x => (x.UserId, x.TargetNodeId, x.Kind, x.Title)).ToHashSet();
        int taskActions = 0, reminderActions = 0, skippedDue = 0;

        foreach (var item in items)
        {
            if (!nodeIds.Contains(item.ItemId)) continue;

            bool isTodo = item.ItemType == ItemType.Todo;
            bool isReminderType = item.ItemType == ItemType.Reminder;
            bool hasReminderDate = item.ReminderDate is not null;

            if (isTodo)
            {
                if (AddActionIfNew(db, existingActions, new ActionItem
                {
                    UserId = item.UserId,
                    Kind = ActionKind.Task,
                    Status = MapStatus(item.Status),
                    Priority = MapPriority(item.Priority),
                    Title = item.Title,
                    DueDate = item.DueDate,
                    CompletedAt = item.CompletedDate,
                    TargetNodeId = item.ItemId,
                }))
                    taskActions++;
            }

            if (isReminderType || hasReminderDate)
            {
                if (AddActionIfNew(db, existingActions, new ActionItem
                {
                    UserId = item.UserId,
                    Kind = ActionKind.Reminder,
                    Status = MapStatus(item.Status),
                    Title = item.Title,
                    RemindAt = item.ReminderDate,
                    CompletedAt = item.CompletedDate,
                    TargetNodeId = item.ItemId,
                }))
                    reminderActions++;
            }

            // Locked: a stray DueDate on a non-actionable item does NOT invent a Task.
            if (!isTodo && !isReminderType && !hasReminderDate && item.DueDate is not null)
            {
                if (await LogAuditIfNewAsync(db, "SkippedDueDate", item.ItemId,
                        $"Item {item.ItemId} '{item.Title}' had DueDate {item.DueDate:o} but is not a Todo/Reminder; no Task invented."))
                    skippedDue++;
            }
        }

        // ---- ActivityLog.NodeId backfill ----
        var logsToLink = await db.ActivityLogs
            .Where(a => a.ItemId != null && a.NodeId == null).ToListAsync();
        int logsLinked = 0;
        foreach (var log in logsToLink)
        {
            if (nodeIds.Contains(log.ItemId!.Value))
            {
                log.NodeId = log.ItemId;
                logsLinked++;
            }
        }

        await db.SaveChangesAsync();

        // Reseed the Node identity so new nodes don't collide with preserved IDs.
        await ReseedNodeIdentityAsync();

        return new BackfillResult(nodesCreated, nodeTagsCreated, relationsCreated, containsCreated,
            taskActions, reminderActions, logsLinked, skippedDue, unmapped);
    }

    /// <summary>Inserts Items as Nodes preserving primary keys. SQL Server uses IDENTITY_INSERT.</summary>
    private async Task<int> BackfillNodesAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var existing = (await db.Nodes.Select(n => n.NodeId).ToListAsync()).ToHashSet();
        var items = await db.Items.AsNoTracking().Where(i => !existing.Contains(i.ItemId)).ToListAsync();
        if (items.Count == 0) return 0;

        if (db.Database.IsSqlServer())
        {
            // Set-based, identity-preserving insert in a single batch.
            const string sql = @"
SET IDENTITY_INSERT [Nodes] ON;
INSERT INTO [Nodes] ([NodeId],[UserId],[Kind],[State],[Title],[Body],[Url],[IsPinned],[IsFavorite],[ArchivedAt],[CreatedAt],[UpdatedAt])
SELECT i.[ItemId], i.[UserId],
    CASE i.[ItemType]
        WHEN 'Note' THEN 'Note' WHEN 'Thought' THEN 'Note' WHEN 'Bookmark' THEN 'Bookmark'
        WHEN 'Idea' THEN 'Idea' WHEN 'Reference' THEN 'Reference' WHEN 'List' THEN 'List'
        WHEN 'Todo' THEN 'Note' WHEN 'Reminder' THEN 'Note' ELSE 'Unclassified' END,
    CASE WHEN i.[ArchivedAt] IS NOT NULL THEN 'Archived' ELSE 'Active' END,
    i.[Title], i.[Body], i.[Url], i.[IsPinned], i.[IsFavorite], i.[ArchivedAt], i.[CreatedAt], i.[UpdatedAt]
FROM [Items] i
WHERE NOT EXISTS (SELECT 1 FROM [Nodes] n WHERE n.[NodeId] = i.[ItemId]);
SET IDENTITY_INSERT [Nodes] OFF;";
            await db.Database.ExecuteSqlRawAsync(sql);
        }
        else
        {
            // Provider-agnostic fallback (InMemory honours explicit key values).
            foreach (var i in items)
            {
                db.Nodes.Add(new Node
                {
                    NodeId = i.ItemId,
                    UserId = i.UserId,
                    Kind = MapKind(i.ItemType),
                    State = i.ArchivedAt is not null ? NodeState.Archived : NodeState.Active,
                    Title = i.Title,
                    Body = i.Body,
                    Url = i.Url,
                    IsPinned = i.IsPinned,
                    IsFavorite = i.IsFavorite,
                    ArchivedAt = i.ArchivedAt,
                    CreatedAt = i.CreatedAt,
                    UpdatedAt = i.UpdatedAt,
                });
            }
            await db.SaveChangesAsync();
        }
        return items.Count;
    }

    private async Task ReseedNodeIdentityAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        if (!db.Database.IsSqlServer()) return;
        // RESEED with no explicit value resets to the current max, so the next
        // identity is MAX+1. Guard against an empty table.
        await db.Database.ExecuteSqlRawAsync(
            "IF EXISTS (SELECT 1 FROM [Nodes]) DBCC CHECKIDENT ('[Nodes]', RESEED);");
    }

    // ---- Helpers ----

    private static (int source, int target) Canonicalize(RelationType rt, int source, int target)
        => rt.IsSymmetric && source > target ? (target, source) : (source, target);

    private static bool AddRelationIfNew(
        NookContext db, HashSet<(string, int, int, int)> existing,
        string userId, int source, int target, int relTypeId, DateTime createdAt, string? note)
    {
        if (source == target) return false;
        var key = (userId, source, target, relTypeId);
        if (existing.Contains(key)) return false;
        db.NodeRelations.Add(new NodeRelation
        {
            UserId = userId,
            SourceNodeId = source,
            TargetNodeId = target,
            RelationTypeId = relTypeId,
            CreatedAt = createdAt,
            Note = note,
        });
        existing.Add(key);
        return true;
    }

    private static bool AddActionIfNew(
        NookContext db, HashSet<(string, int?, ActionKind, string)> existing, ActionItem action)
    {
        var key = (action.UserId, action.TargetNodeId, action.Kind, action.Title);
        if (existing.Contains(key)) return false;
        db.ActionItems.Add(action);
        existing.Add(key);
        return true;
    }

    private static async Task LogAuditAsync(NookContext db, string category, int? itemId, string detail)
    {
        db.MigrationAudits.Add(new MigrationAudit
        {
            Category = category,
            LegacyItemId = itemId,
            Detail = detail,
            CreatedAt = DateTime.UtcNow,
        });
        await Task.CompletedTask;
    }

    private static async Task<bool> LogAuditIfNewAsync(NookContext db, string category, int? itemId, string detail)
    {
        bool exists = await db.MigrationAudits
            .AnyAsync(a => a.Category == category && a.LegacyItemId == itemId);
        if (exists) return false;
        await LogAuditAsync(db, category, itemId, detail);
        return true;
    }

    // ---- Validation ----

    public async Task<ValidationReport> ValidateAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var checks = new List<ValidationCheck>();

        int itemCount = await db.Items.CountAsync();
        int nodeCount = await db.Nodes.CountAsync();
        checks.Add(new("Node count >= Item count", nodeCount >= itemCount,
            $"Items={itemCount}, Nodes={nodeCount}"));

        // Every item has a node with the same id.
        int itemsWithoutNode = await db.Items
            .CountAsync(i => !db.Nodes.Any(n => n.NodeId == i.ItemId));
        checks.Add(new("Every Item has a Node with matching id (NodeId == ItemId)",
            itemsWithoutNode == 0, $"Items without a matching Node: {itemsWithoutNode}"));

        int itemTagCount = await db.ItemTags.CountAsync();
        int migratableItemTags = await db.ItemTags.CountAsync(it => db.Nodes.Any(n => n.NodeId == it.ItemId));
        int nodeTagCount = await db.NodeTags.CountAsync();
        checks.Add(new("NodeTags cover migratable ItemTags",
            nodeTagCount >= migratableItemTags,
            $"ItemTags={itemTagCount} (migratable={migratableItemTags}), NodeTags={nodeTagCount}"));

        int linkCount = await db.ItemLinks.CountAsync();
        int relationCount = await db.NodeRelations.CountAsync();
        checks.Add(new("NodeRelations present for ItemLinks + parents",
            relationCount >= linkCount, $"ItemLinks={linkCount}, NodeRelations={relationCount}"));

        int todoCount = await db.Items.CountAsync(i => i.ItemType == ItemType.Todo);
        int taskActionCount = await db.ActionItems.CountAsync(a => a.Kind == ActionKind.Task);
        checks.Add(new("Task actions cover legacy Todos",
            taskActionCount >= todoCount, $"Todos={todoCount}, TaskActions={taskActionCount}"));

        int reminderSource = await db.Items.CountAsync(i =>
            i.ItemType == ItemType.Reminder || i.ReminderDate != null);
        int reminderActionCount = await db.ActionItems.CountAsync(a => a.Kind == ActionKind.Reminder);
        checks.Add(new("Reminder actions cover legacy reminders",
            reminderActionCount >= reminderSource,
            $"ReminderSources={reminderSource}, ReminderActions={reminderActionCount}"));

        int relTypeCount = await db.RelationTypes.CountAsync(rt => rt.UserId == null);
        checks.Add(new("System relation types seeded",
            relTypeCount >= GraphSeedData.RelationTypes.Count,
            $"System RelationTypes={relTypeCount}/{GraphSeedData.RelationTypes.Count}"));

        int verbCount = await db.Verbs.CountAsync(v => v.UserId == null);
        checks.Add(new("System verbs seeded",
            verbCount >= GraphSeedData.Verbs.Count, $"System Verbs={verbCount}/{GraphSeedData.Verbs.Count}"));

        int orphanLogs = await db.ActivityLogs.CountAsync(a =>
            a.ItemId != null && a.NodeId == null && db.Nodes.Any(n => n.NodeId == a.ItemId));
        checks.Add(new("ActivityLog.NodeId backfilled where the node exists",
            orphanLogs == 0, $"Log rows still missing NodeId: {orphanLogs}"));

        var audits = await db.MigrationAudits
            .OrderBy(a => a.Category).ThenBy(a => a.LegacyItemId)
            .Select(a => $"[{a.Category}] {a.Detail}")
            .ToListAsync();

        return new ValidationReport(checks, audits);
    }

    public async Task<MigrationStatus> GetStatusAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        int items = await db.Items.CountAsync();
        int nodes = await db.Nodes.CountAsync();
        int relTypes = await db.RelationTypes.CountAsync(rt => rt.UserId == null);
        int verbs = await db.Verbs.CountAsync(v => v.UserId == null);
        bool seeded = relTypes >= GraphSeedData.RelationTypes.Count && verbs >= GraphSeedData.Verbs.Count;
        int itemsWithoutNode = await db.Items.CountAsync(i => !db.Nodes.Any(n => n.NodeId == i.ItemId));
        bool complete = seeded && itemsWithoutNode == 0;
        return new MigrationStatus(items, nodes, relTypes, verbs, seeded, complete);
    }
}
