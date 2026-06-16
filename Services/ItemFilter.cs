using Nook.Models;

namespace Nook.Services;

/// <summary>
/// Carries the criteria used to query items on the list and view pages.
/// All properties are optional; an empty filter returns all active items.
/// </summary>
public class ItemFilter
{
    /// <summary>Matched against Title, Body, Url and tag names.</summary>
    public string? SearchText { get; set; }

    public ItemType? ItemType { get; set; }
    public ItemStatus? Status { get; set; }
    public Priority? Priority { get; set; }

    /// <summary>Only items carrying this tag.</summary>
    public int? TagId { get; set; }

    /// <summary>Items due within <see cref="DueSoonDays"/> and not yet done.</summary>
    public bool DueSoon { get; set; }

    /// <summary>Items whose due date has passed and are not yet done.</summary>
    public bool Overdue { get; set; }

    public bool FavoritesOnly { get; set; }
    public bool PinnedOnly { get; set; }

    /// <summary>false = active items only (default); true = archived items only.</summary>
    public bool ShowArchived { get; set; }

    public int DueSoonDays { get; set; } = 7;
}
