using System.ComponentModel.DataAnnotations;

namespace Nook.Models;

/// <summary>A label that can be applied to many items.</summary>
public class Tag
{
    public int TagId { get; set; }

    /// <summary>Owner of this tag. Tags are per-user. FK to ApplicationUser.</summary>
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional display color (a MudBlazor color name or hex string).</summary>
    [MaxLength(50)]
    public string? Color { get; set; }

    /// <summary>Legacy Item assignments (retained until legacy retirement).</summary>
    public ICollection<ItemTag> ItemTags { get; set; } = new List<ItemTag>();

    /// <summary>Node assignments (the graph model).</summary>
    public ICollection<NodeTag> NodeTags { get; set; } = new List<NodeTag>();
}
