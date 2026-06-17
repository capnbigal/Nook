using System.ComponentModel.DataAnnotations;

namespace Nook.Models;

/// <summary>
/// An immutable audit record of a change to an item. Feeds the Log page and the
/// Timeline. ItemId is nullable and ItemTitle is denormalized so a log row
/// survives deletion of its item.
/// </summary>
public class ActivityLog
{
    public int ActivityLogId { get; set; }

    /// <summary>Owner. FK to ApplicationUser.</summary>
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    /// <summary>The affected item, or null if it has since been deleted.</summary>
    public int? ItemId { get; set; }

    /// <summary>Snapshot of the item's title at the time of the event.</summary>
    [MaxLength(300)]
    public string ItemTitle { get; set; } = string.Empty;

    public ActivityType Type { get; set; }

    public DateTime Timestamp { get; set; }

    /// <summary>Optional human-readable detail, e.g. "status Open → Done".</summary>
    [MaxLength(500)]
    public string? Detail { get; set; }
}
