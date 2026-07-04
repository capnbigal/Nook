using Microsoft.EntityFrameworkCore;
using Nook.Data;
using Nook.Models;

namespace Nook.Services;

public sealed class RelationService : IRelationService
{
    private readonly IDbContextFactory<NookContext> _factory;
    private readonly ICurrentUser _currentUser;
    private readonly IActivityService _activity;

    public RelationService(IDbContextFactory<NookContext> factory, ICurrentUser currentUser, IActivityService activity)
    {
        _factory = factory;
        _currentUser = currentUser;
        _activity = activity;
    }

    public async Task<List<RelationType>> GetRelationTypesAsync()
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        return await db.RelationTypes
            .Where(rt => rt.UserId == null || rt.UserId == userId)
            .OrderBy(rt => rt.Category).ThenBy(rt => rt.Name)
            .ToListAsync();
    }

    public async Task<AddRelationResult> AddRelationAsync(
        int sourceNodeId, int targetNodeId, int relationTypeId, string? note = null)
    {
        if (sourceNodeId == targetNodeId) return AddRelationResult.SelfLink;
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();

        // Both endpoints must belong to the current user.
        int owned = await db.Nodes.CountAsync(n =>
            n.UserId == userId && (n.NodeId == sourceNodeId || n.NodeId == targetNodeId));
        if (owned < 2) return AddRelationResult.InvalidNodes;

        var relType = await db.RelationTypes.FirstOrDefaultAsync(rt =>
            rt.RelationTypeId == relationTypeId && (rt.UserId == null || rt.UserId == userId));
        if (relType is null) return AddRelationResult.InvalidType;

        var (s, t) = relType.IsSymmetric && sourceNodeId > targetNodeId
            ? (targetNodeId, sourceNodeId)
            : (sourceNodeId, targetNodeId);

        bool exists = await db.NodeRelations.AnyAsync(r =>
            r.UserId == userId && r.SourceNodeId == s && r.TargetNodeId == t && r.RelationTypeId == relationTypeId);
        if (exists) return AddRelationResult.Duplicate;

        // Also treat the reverse of a symmetric pair as a duplicate (defensive;
        // canonicalisation already prevents it, but guards mixed legacy data).
        if (relType.IsSymmetric)
        {
            bool reverse = await db.NodeRelations.AnyAsync(r =>
                r.UserId == userId && r.SourceNodeId == t && r.TargetNodeId == s && r.RelationTypeId == relationTypeId);
            if (reverse) return AddRelationResult.Duplicate;
        }

        db.NodeRelations.Add(new NodeRelation
        {
            UserId = userId,
            SourceNodeId = s,
            TargetNodeId = t,
            RelationTypeId = relationTypeId,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var titles = await db.Nodes.Where(n => n.NodeId == s || n.NodeId == t)
            .Select(n => new { n.NodeId, n.Title }).ToListAsync();
        var sTitle = titles.FirstOrDefault(x => x.NodeId == s)?.Title ?? "";
        var tTitle = titles.FirstOrDefault(x => x.NodeId == t)?.Title ?? "";
        await _activity.LogNodeAsync(userId, ActivityType.Updated, s, sTitle,
            $"{relType.Name} '{tTitle}'");
        return AddRelationResult.Added;
    }

    public async Task RemoveRelationAsync(int nodeRelationId)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        var rel = await db.NodeRelations
            .FirstOrDefaultAsync(r => r.NodeRelationId == nodeRelationId && r.UserId == userId);
        if (rel is null) return;
        db.NodeRelations.Remove(rel);
        await db.SaveChangesAsync();
    }

    public async Task<NodeConnections> GetConnectionsAsync(int nodeId)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();

        var outgoing = await db.NodeRelations
            .Where(r => r.UserId == userId && r.SourceNodeId == nodeId)
            .Include(r => r.RelationType).Include(r => r.TargetNode)
            .OrderBy(r => r.RelationType!.Category).ThenBy(r => r.RelationType!.Name)
            .Select(r => new Connection(
                r.NodeRelationId, r.TargetNodeId, r.TargetNode!.Title, r.TargetNode.Kind,
                r.RelationType!.Name, true, r.RelationType.Category, r.Note))
            .ToListAsync();

        var backlinks = await db.NodeRelations
            .Where(r => r.UserId == userId && r.TargetNodeId == nodeId)
            .Include(r => r.RelationType).Include(r => r.SourceNode)
            .OrderBy(r => r.RelationType!.Category).ThenBy(r => r.RelationType!.Name)
            .Select(r => new Connection(
                r.NodeRelationId, r.SourceNodeId, r.SourceNode!.Title, r.SourceNode.Kind,
                r.RelationType!.InverseName ?? r.RelationType.Name, false, r.RelationType.Category, r.Note))
            .ToListAsync();

        return new NodeConnections(outgoing, backlinks);
    }
}
