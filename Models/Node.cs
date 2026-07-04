using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Nook.Models;

/// <summary>
/// The universal addressable object in Nook. Every record and entity — notes,
/// people, projects, places, bookmarks, collections, events — is a Node. It
/// carries only the fields common to most things; kind-specific structured data
/// lives in optional 1:1 profile tables (<see cref="Collection"/>, <see cref="EventDetails"/>)
/// and behaviour comes from related tables (relations, actions, events, tags),
/// never from <see cref="NodeKind"/>.
/// </summary>
public class Node
{
    public int NodeId { get; set; }

    /// <summary>Owner of this node. FK to ApplicationUser.</summary>
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    /// <summary>Lightweight classification (display/filter/profile only). Defaults to Unclassified.</summary>
    public NodeKind Kind { get; set; } = NodeKind.Unclassified;

    /// <summary>Lifecycle state. Quick capture defaults to Inbox.</summary>
    public NodeState State { get; set; } = NodeState.Inbox;

    [Required]
    [MaxLength(300)]
    public string Title { get; set; } = string.Empty;

    /// <summary>Free-form body/content. Maps to nvarchar(max).</summary>
    public string? Body { get; set; }

    /// <summary>Optional URL (bookmarks/resources).</summary>
    [MaxLength(1000)]
    public string? Url { get; set; }

    public bool IsPinned { get; set; }
    public bool IsFavorite { get; set; }

    public DateTime? ArchivedAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // ---- Navigation properties ----

    public ICollection<NodeTag> NodeTags { get; set; } = new List<NodeTag>();

    /// <summary>Typed relations where this node is the source.</summary>
    public ICollection<NodeRelation> OutgoingRelations { get; set; } = new List<NodeRelation>();

    /// <summary>Typed relations where this node is the target.</summary>
    public ICollection<NodeRelation> IncomingRelations { get; set; } = new List<NodeRelation>();

    /// <summary>The 1:1 collection profile when <see cref="Kind"/> is Collection.</summary>
    public Collection? Collection { get; set; }

    /// <summary>The 1:1 event profile when <see cref="Kind"/> is Event.</summary>
    public EventDetails? EventDetails { get; set; }

    // ---- Convenience (not mapped) ----

    /// <summary>The tags assigned to this node (requires NodeTags.Tag to be loaded).</summary>
    [NotMapped]
    public IEnumerable<Tag> Tags => NodeTags.Select(nt => nt.Tag).Where(t => t is not null)!;

    [NotMapped]
    public bool IsArchived => State == NodeState.Archived || ArchivedAt is not null;
}
