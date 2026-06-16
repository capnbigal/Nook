namespace Nook.Models;

/// <summary>Join entity for the many-to-many relationship between items and tags.</summary>
public class ItemTag
{
    public int ItemId { get; set; }
    public Item Item { get; set; } = null!;

    public int TagId { get; set; }
    public Tag Tag { get; set; } = null!;
}
