namespace Nook.Services;

/// <summary>Resolves [[wiki-link]] titles to owned nodes and reconciles "mentions" relations.</summary>
public interface IWikiLinkService
{
    /// <summary>Find an owned node by exact Title; if none, quick-capture an Unclassified inbox node. Returns id + "/nodes/{id}".</summary>
    Task<(int nodeId, string url)> ResolveOrCreateAsync(string title, CancellationToken ct = default);

    /// <summary>Diff the given [[titles]] against existing outgoing "mentions" relations from the source; add missing, remove stale.</summary>
    Task ReconcileAsync(int sourceNodeId, IReadOnlyCollection<string> linkedTitles, CancellationToken ct = default);
}
