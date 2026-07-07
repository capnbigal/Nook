using Nook.Models;

namespace Nook.Services;

public sealed record RecentNode(int Id, string Title, NodeKind Kind);
public sealed record Breadcrumb(string Label, string? Href);

/// <summary>Scoped UI state: MRU recents + current breadcrumb trail. Cascaded from WorkspaceShell.</summary>
public sealed class WorkspaceState
{
    private readonly IUserPreferenceService _prefs;
    private readonly INodeService _nodes;
    private List<int> _recentIds = new();

    public WorkspaceState(IUserPreferenceService prefs, INodeService nodes)
    {
        _prefs = prefs;
        _nodes = nodes;
    }

    public IReadOnlyList<RecentNode> Recents { get; private set; } = Array.Empty<RecentNode>();
    public IReadOnlyList<Breadcrumb> Trail { get; private set; } = Array.Empty<Breadcrumb>();
    public event Action? Changed;

    /// <summary>PURE: dedupe, move visited id to front, cap. Unit-tested.</summary>
    public static IReadOnlyList<int> MergeRecentIds(IReadOnlyList<int> current, int visitedId, int cap = 12)
    {
        var list = new List<int>(current.Count + 1) { visitedId };
        foreach (var id in current)
            if (id != visitedId) list.Add(id);
        if (list.Count > cap) list.RemoveRange(cap, list.Count - cap);
        return list;
    }

    /// <summary>Loads persisted recents. Safe during prerender (no JS interop involved).</summary>
    public async Task InitializeAsync()
    {
        _recentIds = (await _prefs.GetRecentIdsAsync()).ToList();
        await HydrateAsync();
    }

    /// <summary>Records a visit: persists to preferences, re-merges the in-memory MRU list, and raises Changed.</summary>
    public async Task NoteVisitedAsync(int nodeId)
    {
        await _prefs.PushRecentAsync(nodeId);
        _recentIds = MergeRecentIds(_recentIds, nodeId).ToList();
        await HydrateAsync();
        Changed?.Invoke();
    }

    public void SetTrail(IReadOnlyList<Breadcrumb> trail)
    {
        Trail = trail;
        Changed?.Invoke();
    }

    private async Task HydrateAsync()
    {
        var hydrated = new List<RecentNode>(_recentIds.Count);
        foreach (var id in _recentIds)
        {
            var n = await _nodes.GetByIdAsync(id);
            if (n is not null) hydrated.Add(new RecentNode(n.NodeId, n.Title, n.Kind));
        }
        Recents = hydrated;
    }
}
