using System.ComponentModel.DataAnnotations;

namespace Nook.Models;

/// <summary>A manual, directional link between two items.</summary>
public class ItemLink
{
    public int ItemLinkId { get; set; }

    public int SourceItemId { get; set; }
    public Item? SourceItem { get; set; }

    public int TargetItemId { get; set; }
    public Item? TargetItem { get; set; }

    /// <summary>Optional description of the relationship (e.g. "related", "blocks").</summary>
    [MaxLength(50)]
    public string? LinkType { get; set; }

    public DateTime CreatedAt { get; set; }
}
