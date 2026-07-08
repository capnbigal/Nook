using Nook.Models;

namespace Nook.Services;

/// <summary>Resolves [[wiki-link]] titles to owned nodes and reconciles "mentions" relations from a node's body.</summary>
public sealed class WikiLinkService : IWikiLinkService
{
    private const string MentionsRelationName = "mentions";
    private readonly INodeService _nodes;
    private readonly IRelationService _relations;

    public WikiLinkService(INodeService nodes, IRelationService relations)
    {
        _nodes = nodes;
        _relations = relations;
    }

    public async Task<(int nodeId, string url)> ResolveOrCreateAsync(string title, CancellationToken ct = default)
    {
        var trimmed = (title ?? string.Empty).Trim();
        if (trimmed.Length == 0) return (0, string.Empty); // sentinel: no link for a blank title

        // Real exact-title lookup, scoped to the current user by NodeService — no substring
        // search window to fall out of, so this can't miss an existing node and duplicate it.
        var match = await _nodes.FindByExactTitleAsync(trimmed, ct);
        var id = match?.NodeId ?? (await _nodes.QuickCaptureAsync(trimmed)).NodeId;
        return (id, $"/nodes/{id}");
    }

    public async Task ReconcileAsync(int sourceNodeId, IReadOnlyCollection<string> linkedTitles, CancellationToken ct = default)
    {
        // Look this up first so a missing "mentions" type can't leave newly-created
        // Inbox nodes behind from the resolve loop below with no relation work done.
        var mentionsType = (await _relations.GetRelationTypesAsync())
            .FirstOrDefault(rt => rt.Name == MentionsRelationName);
        if (mentionsType is null) return; // seed data missing; nothing to reconcile against

        // Resolve desired targets (dedupe by resolved node id; drop self-links and blank-title sentinels).
        var desired = new HashSet<int>();
        foreach (var t in linkedTitles.Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            var (id, _) = await ResolveOrCreateAsync(t, ct);
            if (id != 0 && id != sourceNodeId) desired.Add(id);
        }

        var connections = await _relations.GetConnectionsAsync(sourceNodeId);
        var existing = connections.Outgoing
            .Where(c => c.Label == MentionsRelationName)
            .ToList();
        var existingTargets = existing.Select(c => c.OtherNodeId).ToHashSet();

        // Add missing.
        foreach (var targetId in desired.Where(id => !existingTargets.Contains(id)))
            await _relations.AddRelationAsync(sourceNodeId, targetId, mentionsType.RelationTypeId);

        // Remove stale.
        foreach (var stale in existing.Where(c => !desired.Contains(c.OtherNodeId)))
            await _relations.RemoveRelationAsync(stale.NodeRelationId);
    }
}
