using Microsoft.EntityFrameworkCore;
using Nook.Data;
using Nook.Models;

namespace Nook.Services;

public sealed class NodeService : INodeService
{
    private readonly IDbContextFactory<NookContext> _factory;
    private readonly ICurrentUser _currentUser;
    private readonly IActivityService _activity;

    public NodeService(IDbContextFactory<NookContext> factory, ICurrentUser currentUser, IActivityService activity)
    {
        _factory = factory;
        _currentUser = currentUser;
        _activity = activity;
    }

    private static IQueryable<Node> WithTags(IQueryable<Node> q) =>
        q.Include(n => n.NodeTags).ThenInclude(nt => nt.Tag);

    public async Task<List<Node>> QueryAsync(NodeFilter filter)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        var query = WithTags(db.Nodes).Where(n => n.UserId == userId);

        query = filter.ArchivedOnly
            ? query.Where(n => n.State == NodeState.Archived)
            : query.Where(n => n.State != NodeState.Archived);

        if (filter.State.HasValue) query = query.Where(n => n.State == filter.State.Value);
        if (filter.Kind.HasValue) query = query.Where(n => n.Kind == filter.Kind.Value);
        if (filter.KindsIn is { Count: > 0 })
        {
            var kinds = filter.KindsIn.ToList();
            query = query.Where(n => kinds.Contains(n.Kind));
        }
        if (filter.TagId.HasValue)
            query = query.Where(n => n.NodeTags.Any(nt => nt.TagId == filter.TagId.Value));
        if (filter.PinnedOnly) query = query.Where(n => n.IsPinned);
        if (filter.FavoritesOnly) query = query.Where(n => n.IsFavorite);

        if (filter.UnassignedOnly)
        {
            query = query.Where(n =>
                n.Kind == NodeKind.Unclassified ||
                n.State == NodeState.Inbox ||
                !db.CollectionMemberships.Any(m => m.MemberNodeId == n.NodeId));
        }

        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            var s = filter.SearchText.Trim();
            query = query.Where(n =>
                n.Title.Contains(s) ||
                (n.Body != null && n.Body.Contains(s)) ||
                (n.Url != null && n.Url.Contains(s)) ||
                n.NodeTags.Any(nt => nt.Tag.Name.Contains(s)));
        }

        query = query
            .OrderByDescending(n => n.IsPinned)
            .ThenByDescending(n => n.UpdatedAt);

        if (filter.Take is int t) query = query.Take(t);

        return await query.ToListAsync();
    }

    public async Task<Node?> GetByIdAsync(int id)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Nodes
            .Include(n => n.NodeTags).ThenInclude(nt => nt.Tag)
            .Include(n => n.Collection)
            .Include(n => n.EventDetails)
            .FirstOrDefaultAsync(n => n.NodeId == id && n.UserId == userId);
    }

    public async Task<Node> CreateAsync(Node node, IEnumerable<int>? tagIds = null)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        node.UserId = userId;
        db.Nodes.Add(node);
        if (tagIds != null)
            foreach (var tagId in tagIds.Distinct())
                node.NodeTags.Add(new NodeTag { TagId = tagId });
        await db.SaveChangesAsync();
        await _activity.LogNodeAsync(userId, ActivityType.Created, node.NodeId, node.Title);
        return node;
    }

    public Task<Node> QuickCaptureAsync(string title, string? body = null) =>
        CreateAsync(new Node
        {
            Title = title.Trim(),
            Body = string.IsNullOrWhiteSpace(body) ? null : body,
            Kind = NodeKind.Unclassified,
            State = NodeState.Inbox,
        });

    public async Task UpdateAsync(Node node, IEnumerable<int>? tagIds = null)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        var existing = await db.Nodes.Include(n => n.NodeTags)
            .FirstOrDefaultAsync(n => n.NodeId == node.NodeId && n.UserId == userId);
        if (existing is null) return;

        existing.Title = node.Title;
        existing.Body = node.Body;
        existing.Url = node.Url;
        existing.Kind = node.Kind;
        existing.State = node.State;
        existing.IsPinned = node.IsPinned;
        existing.IsFavorite = node.IsFavorite;
        if (existing.State == NodeState.Archived && existing.ArchivedAt is null)
            existing.ArchivedAt = DateTime.UtcNow;
        if (existing.State != NodeState.Archived)
            existing.ArchivedAt = null;

        if (tagIds is not null)
        {
            var desired = tagIds.Distinct().ToHashSet();
            foreach (var remove in existing.NodeTags.Where(nt => !desired.Contains(nt.TagId)).ToList())
                existing.NodeTags.Remove(remove);
            var current = existing.NodeTags.Select(nt => nt.TagId).ToHashSet();
            foreach (var tagId in desired.Where(t => !current.Contains(t)))
                existing.NodeTags.Add(new NodeTag { NodeId = existing.NodeId, TagId = tagId });
        }

        await db.SaveChangesAsync();
        await _activity.LogNodeAsync(userId, ActivityType.Updated, existing.NodeId, existing.Title);
    }

    public async Task SaveBodyAsync(int nodeId, string? body, CancellationToken ct = default)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync(ct);
        var node = await db.Nodes.FirstOrDefaultAsync(n => n.NodeId == nodeId && n.UserId == userId, ct);
        if (node is null) return;
        node.Body = string.IsNullOrWhiteSpace(body) ? null : body;
        node.UpdatedAt = DateTime.UtcNow; // ApplyTimestamps also bumps this on Modified
        await db.SaveChangesAsync(ct);
    }

    public async Task PromoteAsync(int id, NodeKind kind)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        var node = await db.Nodes.FirstOrDefaultAsync(n => n.NodeId == id && n.UserId == userId);
        if (node is null || node.Kind == kind) return;
        var from = node.Kind;
        node.Kind = kind;
        // Promoting out of the inbox is the natural "I've organised this" signal.
        if (node.State == NodeState.Inbox && kind != NodeKind.Unclassified)
            node.State = NodeState.Active;
        await db.SaveChangesAsync();
        await _activity.LogNodeAsync(userId, ActivityType.Updated, node.NodeId, node.Title,
            $"kind {from} → {kind}");
    }

    public async Task SetStateAsync(int id, NodeState state) =>
        await MutateAsync(id, n =>
        {
            n.State = state;
            n.ArchivedAt = state == NodeState.Archived ? DateTime.UtcNow : null;
        }, state == NodeState.Archived ? ActivityType.Archived : ActivityType.Updated);

    public Task ArchiveAsync(int id) => SetStateAsync(id, NodeState.Archived);

    public async Task RestoreAsync(int id) =>
        await MutateAsync(id, n => { n.State = NodeState.Active; n.ArchivedAt = null; },
            ActivityType.Unarchived);

    public Task TogglePinAsync(int id) =>
        MutateAsync(id, n => n.IsPinned = !n.IsPinned, ActivityType.Updated);

    public Task ToggleFavoriteAsync(int id) =>
        MutateAsync(id, n => n.IsFavorite = !n.IsFavorite, ActivityType.Updated);

    private async Task MutateAsync(int id, Action<Node> mutate, ActivityType type)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        var node = await db.Nodes.FirstOrDefaultAsync(n => n.NodeId == id && n.UserId == userId);
        if (node is null) return;
        mutate(node);
        await db.SaveChangesAsync();
        await _activity.LogNodeAsync(userId, type, node.NodeId, node.Title);
    }

    public async Task DeleteAsync(int id)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        var node = await db.Nodes.FirstOrDefaultAsync(n => n.NodeId == id && n.UserId == userId);
        if (node is null) return;

        // Clean up references that are Restrict (would otherwise block the delete).
        var relations = await db.NodeRelations
            .Where(r => r.SourceNodeId == id || r.TargetNodeId == id).ToListAsync();
        db.NodeRelations.RemoveRange(relations);

        var memberships = await db.CollectionMemberships
            .Where(m => m.MemberNodeId == id).ToListAsync();
        db.CollectionMemberships.RemoveRange(memberships);

        var contexts = await db.ActionContexts.Where(c => c.NodeId == id).ToListAsync();
        db.ActionContexts.RemoveRange(contexts);

        var participations = await db.EventParticipants
            .Where(p => p.ParticipantNodeId == id).ToListAsync();
        db.EventParticipants.RemoveRange(participations);

        await db.ActionItems.Where(a => a.TargetNodeId == id)
            .ForEachAsync(a => a.TargetNodeId = null);
        await db.EventDetails.Where(e => e.SubjectNodeId == id)
            .ForEachAsync(e => e.SubjectNodeId = null);
        await db.EventDetails.Where(e => e.ObjectNodeId == id)
            .ForEachAsync(e => e.ObjectNodeId = null);
        await db.EventDetails.Where(e => e.PlaceNodeId == id)
            .ForEachAsync(e => e.PlaceNodeId = null);
        await db.Users.Where(u => u.SelfNodeId == id)
            .ForEachAsync(u => u.SelfNodeId = null);

        var title = node.Title;
        db.Nodes.Remove(node); // cascades NodeTags, Collection(+memberships), EventDetails(+participants)
        await db.SaveChangesAsync();
        await _activity.LogNodeAsync(userId, ActivityType.Deleted, null, title);
    }

    // ---- Related / dashboards ----

    public async Task<List<Node>> GetRelatedByTagsAsync(int id, int max = 8)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        var tagIds = await db.NodeTags.Where(nt => nt.NodeId == id).Select(nt => nt.TagId).ToListAsync();
        if (tagIds.Count == 0) return new();
        return await WithTags(db.Nodes)
            .Where(n => n.UserId == userId && n.NodeId != id && n.State != NodeState.Archived
                        && n.NodeTags.Any(nt => tagIds.Contains(nt.TagId)))
            .OrderByDescending(n => n.NodeTags.Count(nt => tagIds.Contains(nt.TagId)))
            .ThenByDescending(n => n.UpdatedAt)
            .Take(max).ToListAsync();
    }

    private async Task<List<Node>> ScopedActive(Func<IQueryable<Node>, IQueryable<Node>> shape)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        var q = WithTags(db.Nodes).Where(n => n.UserId == userId && n.State != NodeState.Archived);
        return await shape(q).ToListAsync();
    }

    public Task<List<Node>> GetInboxAsync(int count = 50) =>
        ScopedActive(q => q.Where(n => n.State == NodeState.Inbox)
            .OrderByDescending(n => n.CreatedAt).Take(count));

    public Task<List<Node>> GetRecentlyUpdatedAsync(int count = 8) =>
        ScopedActive(q => q.OrderByDescending(n => n.UpdatedAt).Take(count));

    public Task<List<Node>> GetPinnedAsync(int count = 10) =>
        ScopedActive(q => q.Where(n => n.IsPinned).OrderByDescending(n => n.UpdatedAt).Take(count));

    public Task<List<Node>> GetFavoritesAsync(int count = 10) =>
        ScopedActive(q => q.Where(n => n.IsFavorite).OrderByDescending(n => n.UpdatedAt).Take(count));

    public async Task<int> CountByStateAsync(NodeState state)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Nodes.CountAsync(n => n.UserId == userId && n.State == state);
    }
}
