using Microsoft.EntityFrameworkCore;
using Nook.Models;
using Nook.Services;
using Xunit;

namespace Nook.Tests;

/// <summary>
/// Real SQL Server (LocalDB) coverage for the schema migration and the graph
/// backfill. These exercise IDENTITY_INSERT, identity reseeding, filtered unique
/// indexes and FK restrictions — none of which EF InMemory models. Each test gets
/// its own database. If LocalDB is not reachable the tests skip with an explicit
/// reason (never a false pass).
/// </summary>
public class MigrationIntegrationTests
{
    private const string U = "user-1";

    private static async Task EnsureUserAsync(SqlServerFixture fx, params string[] ids)
    {
        await using var db = fx.CreateDbContext();
        foreach (var id in ids)
            if (!await db.Users.AnyAsync(u => u.Id == id))
                db.Users.Add(new ApplicationUser { Id = id, UserName = id, Email = id + "@test.local" });
        await db.SaveChangesAsync();
    }

    private static async Task SeedLegacyAsync(SqlServerFixture fx)
    {
        await EnsureUserAsync(fx, U);
        await using var db = fx.CreateDbContext();
        var work = new Tag { Name = "work", Color = "#1E88E5", UserId = U };
        db.Tags.Add(work);
        await db.SaveChangesAsync();

        var note = new Item { Title = "A note", ItemType = ItemType.Note, Status = ItemStatus.Open, UserId = U };
        var todo = new Item { Title = "A todo", ItemType = ItemType.Todo, Status = ItemStatus.Done, UserId = U,
                              Priority = Priority.High, DueDate = DateTime.UtcNow.AddDays(1),
                              CompletedDate = DateTime.UtcNow };
        var reminder = new Item { Title = "A reminder", ItemType = ItemType.Reminder, Status = ItemStatus.Open,
                                  UserId = U, ReminderDate = DateTime.UtcNow.AddDays(2) };
        var bookmark = new Item { Title = "A bookmark", ItemType = ItemType.Bookmark, Status = ItemStatus.Open,
                                  UserId = U, Url = "https://example.com" };
        var strayDue = new Item { Title = "Note with stray due date", ItemType = ItemType.Note,
                                  Status = ItemStatus.Open, UserId = U, DueDate = DateTime.UtcNow.AddDays(3) };
        db.Items.AddRange(note, todo, reminder, bookmark, strayDue);
        await db.SaveChangesAsync();

        var child = new Item { Title = "child", ItemType = ItemType.Note, Status = ItemStatus.Open,
                               UserId = U, ParentItemId = note.ItemId };
        db.Items.Add(child);
        db.ItemLinks.Add(new ItemLink { SourceItemId = note.ItemId, TargetItemId = bookmark.ItemId,
                                        CreatedAt = DateTime.UtcNow });
        db.ItemTags.Add(new ItemTag { ItemId = note.ItemId, TagId = work.TagId });
        db.ActivityLogs.Add(new ActivityLog { UserId = U, Type = ActivityType.Created, ItemId = note.ItemId,
                                              ItemTitle = "A note", Timestamp = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    [SqlServerFact]
    public async Task Backfill_migrates_all_legacy_data_preserving_ids_and_is_idempotent()
    {
        using var fx = new SqlServerFixture();
        await SeedLegacyAsync(fx);
        var mig = new GraphMigrationService(fx);

        await mig.BackfillAsync();

        await using (var db = fx.CreateDbContext())
        {
            int itemCount = await db.Items.CountAsync();
            int nodeCount = await db.Nodes.CountAsync();
            Assert.Equal(itemCount, nodeCount);

            var items = await db.Items.Select(i => i.ItemId).ToListAsync();
            foreach (var id in items)
                Assert.True(await db.Nodes.AnyAsync(n => n.NodeId == id), $"missing node {id}");

            Assert.True(await db.ActionItems.AnyAsync(a => a.Kind == ActionKind.Task && a.Status == ActionStatus.Done));
            Assert.True(await db.ActionItems.AnyAsync(a => a.Kind == ActionKind.Reminder));

            Assert.True(await db.NodeTags.AnyAsync());
            Assert.True(await db.NodeRelations.AnyAsync());
            var containsType = await db.RelationTypes.FirstAsync(rt => rt.Name == "contains");
            Assert.True(await db.NodeRelations.AnyAsync(r => r.RelationTypeId == containsType.RelationTypeId));

            Assert.True(await db.MigrationAudits.AnyAsync(a => a.Category == "SkippedDueDate"));

            Assert.False(await db.ActivityLogs.AnyAsync(a => a.ItemId != null && a.NodeId == null));
        }

        var second = await mig.BackfillAsync();
        Assert.Equal(0, second.NodesCreated);
        Assert.Equal(0, second.TaskActionsCreated);
        Assert.Equal(0, second.ReminderActionsCreated);
        Assert.Equal(0, second.RelationsCreated);

        var report = await mig.ValidateAsync();
        Assert.True(report.AllPassed, string.Join("; ", report.Checks.Where(c => !c.Passed).Select(c => c.Name)));
    }

    [SqlServerFact]
    public async Task Node_identity_reseeds_so_new_nodes_do_not_collide()
    {
        using var fx = new SqlServerFixture();
        await SeedLegacyAsync(fx);
        var mig = new GraphMigrationService(fx);
        await mig.BackfillAsync();

        int maxItemId;
        await using (var db = fx.CreateDbContext())
            maxItemId = await db.Items.MaxAsync(i => i.ItemId);

        var nodeSvc = new NodeService(fx, new FakeCurrentUser(U), new ActivityService(fx));
        var fresh = await nodeSvc.CreateAsync(new Node { Title = "post-migration" });

        Assert.True(fresh.NodeId > maxItemId, $"new NodeId {fresh.NodeId} should exceed max ItemId {maxItemId}");
    }

    [SqlServerFact]
    public async Task Filtered_unique_index_allows_one_system_relation_type_per_name()
    {
        using var fx = new SqlServerFixture();
        var mig = new GraphMigrationService(fx);
        await mig.SeedSystemDataAsync();
        await mig.SeedSystemDataAsync();

        await using var db = fx.CreateDbContext();
        int relatedTo = await db.RelationTypes.CountAsync(rt => rt.UserId == null && rt.Name == "related to");
        Assert.Equal(1, relatedTo);
    }

    [SqlServerFact]
    public async Task Cross_user_isolation_holds_on_sql_server()
    {
        using var fx = new SqlServerFixture();
        await EnsureUserAsync(fx, "a", "b");
        var activity = new ActivityService(fx);
        var a = new NodeService(fx, new FakeCurrentUser("a"), activity);
        var b = new NodeService(fx, new FakeCurrentUser("b"), activity);
        await a.CreateAsync(new Node { Title = "a's node", State = NodeState.Active });

        Assert.Single(await a.QueryAsync(new NodeFilter()));
        Assert.Empty(await b.QueryAsync(new NodeFilter()));
    }
}
