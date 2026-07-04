using Microsoft.EntityFrameworkCore;
using Nook.Data;
using Nook.Models;

namespace Nook.Services;

public sealed class CollectionService : ICollectionService
{
    private readonly IDbContextFactory<NookContext> _factory;
    private readonly ICurrentUser _currentUser;
    private readonly IActivityService _activity;

    public CollectionService(IDbContextFactory<NookContext> factory, ICurrentUser currentUser, IActivityService activity)
    {
        _factory = factory;
        _currentUser = currentUser;
        _activity = activity;
    }

    public async Task<Node> CreateAsync(string title, CollectionKind kind, string? body = null, string? color = null)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        var trimmed = title.Trim();
        await using var db = await _factory.CreateDbContextAsync();
        await ThrowIfNameTakenAsync(db, userId, trimmed);
        var node = NewCollectionNode(userId, trimmed, kind, body, color);
        db.Nodes.Add(node);
        await db.SaveChangesAsync();
        await _activity.LogNodeAsync(userId, ActivityType.Created, node.NodeId, node.Title, $"collection ({kind})");
        return node;
    }

    public async Task<Node> CreateAndAddMemberAsync(string title, CollectionKind kind, string? body, int memberNodeId)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        var trimmed = title.Trim();
        await using var db = await _factory.CreateDbContextAsync();

        // The member must be one of the current user's nodes.
        if (!await db.Nodes.AnyAsync(n => n.NodeId == memberNodeId && n.UserId == userId))
            throw new InvalidOperationException("The node to add to the collection was not found.");
        await ThrowIfNameTakenAsync(db, userId, trimmed);

        var node = NewCollectionNode(userId, trimmed, kind, body, color: null);
        db.Nodes.Add(node);
        // Membership references the new collection via its navigation, so EF fixes up
        // CollectionNodeId after the node/collection insert — one SaveChanges, one transaction.
        db.CollectionMemberships.Add(new CollectionMembership
        {
            Collection = node.Collection,
            MemberNodeId = memberNodeId,
            UserId = userId,
            SortOrder = 0,
            AddedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        await _activity.LogNodeAsync(userId, ActivityType.Created, node.NodeId, node.Title, $"collection ({kind})");
        return node;
    }

    public async Task<bool> NameExistsAsync(string name)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        var trimmed = name.Trim().ToLower();
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Collections.AnyAsync(c =>
            c.Node!.UserId == userId && c.Node.State != NodeState.Archived && c.Node.Title.ToLower() == trimmed);
    }

    private static Node NewCollectionNode(string userId, string title, CollectionKind kind, string? body, string? color) =>
        new()
        {
            UserId = userId,
            Kind = NodeKind.Collection,
            State = NodeState.Active,
            Title = title,
            Body = string.IsNullOrWhiteSpace(body) ? null : body,
            Collection = new Collection
            {
                Kind = kind,
                IsOrdered = kind is CollectionKind.Queue or CollectionKind.List,
                Color = color,
            },
        };

    private static async Task ThrowIfNameTakenAsync(NookContext db, string userId, string trimmedTitle)
    {
        var lower = trimmedTitle.ToLower();
        bool taken = await db.Collections.AnyAsync(c =>
            c.Node!.UserId == userId && c.Node.State != NodeState.Archived && c.Node.Title.ToLower() == lower);
        if (taken)
            throw new InvalidOperationException($"A collection named \"{trimmedTitle}\" already exists.");
    }

    public async Task UpdateAsync(int collectionNodeId, string title, string? body, CollectionKind kind, string? color)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        var node = await db.Nodes.Include(n => n.Collection)
            .FirstOrDefaultAsync(n => n.NodeId == collectionNodeId && n.UserId == userId
                                      && n.Kind == NodeKind.Collection);
        if (node?.Collection is null) return;
        node.Title = title.Trim();
        node.Body = string.IsNullOrWhiteSpace(body) ? null : body;
        node.Collection.Kind = kind;
        node.Collection.IsOrdered = kind is CollectionKind.Queue or CollectionKind.List;
        node.Collection.Color = color;
        await db.SaveChangesAsync();
        await _activity.LogNodeAsync(userId, ActivityType.Updated, node.NodeId, node.Title);
    }

    public async Task<List<CollectionSummary>> GetCollectionsAsync(bool includeArchived = false)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        var query = db.Collections
            .Include(c => c.Node)
            .Where(c => c.Node!.UserId == userId);
        if (!includeArchived)
            query = query.Where(c => c.Node!.State != NodeState.Archived);
        return await query
            .OrderByDescending(c => c.Node!.IsPinned).ThenBy(c => c.Node!.Title)
            .Select(c => new CollectionSummary(c.Node!, c.Kind, c.IsOrdered, c.Memberships.Count))
            .ToListAsync();
    }

    public async Task<Collection?> GetAsync(int collectionNodeId)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Collections.Include(c => c.Node)
            .FirstOrDefaultAsync(c => c.NodeId == collectionNodeId && c.Node!.UserId == userId);
    }

    public async Task<List<Node>> GetMembersAsync(int collectionNodeId)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        return await db.CollectionMemberships
            .Where(m => m.CollectionNodeId == collectionNodeId && m.UserId == userId)
            .OrderBy(m => m.SortOrder).ThenBy(m => m.AddedAt)
            .Include(m => m.MemberNode).ThenInclude(n => n!.NodeTags).ThenInclude(nt => nt.Tag)
            .Select(m => m.MemberNode!)
            .ToListAsync();
    }

    public async Task<bool> AddMemberAsync(int collectionNodeId, int memberNodeId)
    {
        if (collectionNodeId == memberNodeId) return false;
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();

        // Collection and member must both belong to the user.
        bool collectionOk = await db.Collections.AnyAsync(c =>
            c.NodeId == collectionNodeId && c.Node!.UserId == userId);
        bool memberOk = await db.Nodes.AnyAsync(n => n.NodeId == memberNodeId && n.UserId == userId);
        if (!collectionOk || !memberOk) return false;

        bool exists = await db.CollectionMemberships.AnyAsync(m =>
            m.CollectionNodeId == collectionNodeId && m.MemberNodeId == memberNodeId);
        if (exists) return false;

        int nextOrder = await db.CollectionMemberships
            .Where(m => m.CollectionNodeId == collectionNodeId)
            .Select(m => (int?)m.SortOrder).MaxAsync() ?? -1;

        db.CollectionMemberships.Add(new CollectionMembership
        {
            CollectionNodeId = collectionNodeId,
            MemberNodeId = memberNodeId,
            UserId = userId,
            SortOrder = nextOrder + 1,
            AddedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return true;
    }

    public async Task RemoveMemberAsync(int collectionNodeId, int memberNodeId)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        var m = await db.CollectionMemberships.FirstOrDefaultAsync(x =>
            x.CollectionNodeId == collectionNodeId && x.MemberNodeId == memberNodeId && x.UserId == userId);
        if (m is null) return;
        db.CollectionMemberships.Remove(m);
        await db.SaveChangesAsync();
    }

    public async Task MoveMemberAsync(int collectionNodeId, int memberNodeId, bool up)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        var members = await db.CollectionMemberships
            .Where(m => m.CollectionNodeId == collectionNodeId && m.UserId == userId)
            .OrderBy(m => m.SortOrder).ThenBy(m => m.AddedAt)
            .ToListAsync();
        // Normalise sort orders 0..n-1 first, so swaps are always well-defined.
        for (int i = 0; i < members.Count; i++) members[i].SortOrder = i;

        int idx = members.FindIndex(m => m.MemberNodeId == memberNodeId);
        if (idx < 0) return;
        int swap = up ? idx - 1 : idx + 1;
        if (swap < 0 || swap >= members.Count) { await db.SaveChangesAsync(); return; }

        (members[idx].SortOrder, members[swap].SortOrder) = (members[swap].SortOrder, members[idx].SortOrder);
        await db.SaveChangesAsync();
    }

    public async Task<List<Node>> GetCollectionsForNodeAsync(int memberNodeId)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        return await db.CollectionMemberships
            .Where(m => m.MemberNodeId == memberNodeId && m.UserId == userId)
            .Include(m => m.Collection).ThenInclude(c => c!.Node)
            .Select(m => m.Collection!.Node!)
            .OrderBy(n => n.Title)
            .ToListAsync();
    }

    public async Task<List<CollectionSummary>> GetCollectionSummariesForNodeAsync(int memberNodeId)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        return await db.CollectionMemberships
            .Where(m => m.MemberNodeId == memberNodeId && m.UserId == userId)
            .Include(m => m.Collection).ThenInclude(c => c!.Node)
            .OrderBy(m => m.Collection!.Node!.Title)
            .Select(m => new CollectionSummary(
                m.Collection!.Node!, m.Collection.Kind, m.Collection.IsOrdered, m.Collection.Memberships.Count))
            .ToListAsync();
    }
}
