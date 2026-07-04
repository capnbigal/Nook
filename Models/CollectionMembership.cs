namespace Nook.Models;

/// <summary>
/// Ordered many-to-many membership of a node in a collection. The same node may
/// belong to many collections without duplication.
/// </summary>
public class CollectionMembership
{
    /// <summary>The NodeId of the collection-backing node.</summary>
    public int CollectionNodeId { get; set; }
    public Collection? Collection { get; set; }

    /// <summary>The member node.</summary>
    public int MemberNodeId { get; set; }
    public Node? MemberNode { get; set; }

    /// <summary>Owner. Denormalised for scoping; both nodes belong to this user.</summary>
    public string UserId { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public DateTime AddedAt { get; set; }
}
