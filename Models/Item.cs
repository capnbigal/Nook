using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Nook.Models;

/// <summary>
/// The single unified entity for all captured information — notes, reminders,
/// bookmarks, thoughts, lists, todos, ideas and references. <see cref="ItemType"/>
/// distinguishes the kind of item so everything can live in one table.
/// </summary>
public class Item
{
    public int ItemId { get; set; }

    [Required]
    [MaxLength(300)]
    public string Title { get; set; } = string.Empty;

    /// <summary>Free-form body/content. Maps to nvarchar(max).</summary>
    public string? Body { get; set; }

    public ItemType ItemType { get; set; } = ItemType.Note;

    public ItemStatus Status { get; set; } = ItemStatus.Open;

    public Priority? Priority { get; set; }

    public DateTime? DueDate { get; set; }

    public DateTime? ReminderDate { get; set; }

    public DateTime? CompletedDate { get; set; }

    /// <summary>Primarily used for bookmarks.</summary>
    [MaxLength(1000)]
    public string? Url { get; set; }

    /// <summary>Optional parent for list items, sub-tasks or grouped thoughts.</summary>
    public int? ParentItemId { get; set; }
    public Item? Parent { get; set; }
    public ICollection<Item> Children { get; set; } = new List<Item>();

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? ArchivedAt { get; set; }

    public bool IsPinned { get; set; }
    public bool IsFavorite { get; set; }

    // ---- Navigation properties ----

    public ICollection<ItemTag> ItemTags { get; set; } = new List<ItemTag>();

    /// <summary>Manual links where this item is the source.</summary>
    public ICollection<ItemLink> OutgoingLinks { get; set; } = new List<ItemLink>();

    /// <summary>Manual links where this item is the target.</summary>
    public ICollection<ItemLink> IncomingLinks { get; set; } = new List<ItemLink>();

    // ---- Convenience (not mapped to the database) ----

    /// <summary>The tags assigned to this item (requires ItemTags.Tag to be loaded).</summary>
    [NotMapped]
    public IEnumerable<Tag> Tags => ItemTags.Select(it => it.Tag).Where(t => t is not null);

    [NotMapped]
    public bool IsArchived => ArchivedAt is not null;
}
