using Nook.Models;

namespace Nook.Services;

/// <summary>Criteria for querying nodes on list and search pages. All optional.</summary>
public class NodeFilter
{
    /// <summary>Matched against Title, Body, Url and tag names.</summary>
    public string? SearchText { get; set; }

    public NodeKind? Kind { get; set; }

    /// <summary>Restrict to any of these kinds (e.g. the record kinds for "Notes &amp; Records").</summary>
    public IReadOnlyCollection<NodeKind>? KindsIn { get; set; }

    public NodeState? State { get; set; }

    public int? TagId { get; set; }

    public bool PinnedOnly { get; set; }
    public bool FavoritesOnly { get; set; }

    /// <summary>false = exclude archived (default); true = only archived.</summary>
    public bool ArchivedOnly { get; set; }

    /// <summary>
    /// The "Unassigned" system filter: Unclassified kind, or Inbox state, or no
    /// collection membership. Never hides nodes elsewhere.
    /// </summary>
    public bool UnassignedOnly { get; set; }

    /// <summary>The record kinds surfaced by the "Notes &amp; Records" view.</summary>
    public static readonly IReadOnlyCollection<NodeKind> RecordKinds = new[]
    {
        NodeKind.Unclassified, NodeKind.Note, NodeKind.Journal, NodeKind.Observation,
        NodeKind.Idea, NodeKind.Reference, NodeKind.Bookmark, NodeKind.List,
    };
}
