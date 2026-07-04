using System.ComponentModel.DataAnnotations;

namespace Nook.Models;

/// <summary>
/// A required 1:1 profile of a <see cref="Node"/> whose <see cref="Node.Kind"/>
/// is Collection. The node supplies title, body/description, tags, relations,
/// pin/favorite/archive, search and backlinks; this profile adds collection-
/// specific fields. Collections organise and order nodes via
/// <see cref="CollectionMembership"/>; they are not checklists.
/// </summary>
public class Collection
{
    /// <summary>PK and FK to the backing node (1:1).</summary>
    public int NodeId { get; set; }
    public Node? Node { get; set; }

    public CollectionKind Kind { get; set; } = CollectionKind.Plain;

    /// <summary>Whether membership order is meaningful (queues are ordered by default).</summary>
    public bool IsOrdered { get; set; }

    /// <summary>Optional display color (MudBlazor color name or hex).</summary>
    [MaxLength(50)]
    public string? Color { get; set; }

    public ICollection<CollectionMembership> Memberships { get; set; } = new List<CollectionMembership>();
}
