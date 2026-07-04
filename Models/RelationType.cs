using System.ComponentModel.DataAnnotations;

namespace Nook.Models;

/// <summary>
/// A typed vocabulary term for relations between nodes. System types (UserId
/// null) are shared by everyone; user-owned types (UserId set) are reserved for
/// future custom relation types. Directional types have an <see cref="InverseName"/>
/// used to label backlinks; symmetric types read the same both ways.
/// </summary>
public class RelationType
{
    public int RelationTypeId { get; set; }

    /// <summary>Null = system-defined (shared). Set = owned by a specific user (future custom types).</summary>
    public string? UserId { get; set; }
    public ApplicationUser? User { get; set; }

    [Required]
    [MaxLength(60)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Label shown on the target's backlink panel for directional types. Null when symmetric.</summary>
    [MaxLength(60)]
    public string? InverseName { get; set; }

    public bool IsSymmetric { get; set; }

    public RelationCategory Category { get; set; } = RelationCategory.General;

    public bool IsSystem { get; set; }

    public ICollection<NodeRelation> Relations { get; set; } = new List<NodeRelation>();
}
