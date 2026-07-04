using System.ComponentModel.DataAnnotations;

namespace Nook.Models;

/// <summary>
/// A durable record of a notable decision or skip made during the graph backfill
/// (e.g. a legacy DueDate on a non-actionable item that was intentionally not
/// turned into a Task, or an unmapped legacy link label). Feeds the migration
/// validation report and preserves an audit trail of what the backfill did.
/// </summary>
public class MigrationAudit
{
    public int MigrationAuditId { get; set; }

    /// <summary>Machine-readable category, e.g. "SkippedDueDate", "UnmappedLinkLabel".</summary>
    [Required]
    [MaxLength(80)]
    public string Category { get; set; } = string.Empty;

    /// <summary>The legacy Item/link this note concerns, when applicable.</summary>
    public int? LegacyItemId { get; set; }

    /// <summary>Human-readable detail.</summary>
    [MaxLength(1000)]
    public string? Detail { get; set; }

    public DateTime CreatedAt { get; set; }
}
