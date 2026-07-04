using System.ComponentModel.DataAnnotations;

namespace Nook.Models;

/// <summary>
/// A typed, directed (or symmetric) connection between two nodes owned by the
/// same user. Replaces the legacy <see cref="ItemLink"/>. For symmetric relation
/// types the pair is canonicalised (smaller NodeId as source) so A–B and B–A
/// cannot both exist.
/// </summary>
public class NodeRelation
{
    public int NodeRelationId { get; set; }

    /// <summary>Owner. Both endpoints must belong to this same user.</summary>
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    public int SourceNodeId { get; set; }
    public Node? SourceNode { get; set; }

    public int TargetNodeId { get; set; }
    public Node? TargetNode { get; set; }

    public int RelationTypeId { get; set; }
    public RelationType? RelationType { get; set; }

    /// <summary>Optional free-text context for why the relation exists.</summary>
    [MaxLength(500)]
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }
}
