using System.ComponentModel.DataAnnotations;

namespace Nook.Models;

/// <summary>A label that can be applied to many items.</summary>
public class Tag
{
    public int TagId { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional display color (a MudBlazor color name or hex string).</summary>
    [MaxLength(50)]
    public string? Color { get; set; }

    public ICollection<ItemTag> ItemTags { get; set; } = new List<ItemTag>();
}
