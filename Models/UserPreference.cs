using System.ComponentModel.DataAnnotations;

namespace Nook.Models;

/// <summary>Per-user workspace preferences (one row per user).</summary>
public class UserPreference
{
    public int Id { get; set; }

    /// <summary>Owner. FK to AspNetUsers; unique (one row per user).</summary>
    public string UserId { get; set; } = string.Empty;

    public bool IsDarkMode { get; set; }
    public bool SidebarCollapsed { get; set; }

    /// <summary>Denormalised last-opened node id (no FK — must survive node deletion).</summary>
    public int? LastOpenedNodeId { get; set; }

    /// <summary>MRU recent node ids, most-recent-first, comma-separated. Capped at 12.</summary>
    [MaxLength(200)]
    public string RecentNodeIdsCsv { get; set; } = "";

    public DateTime UpdatedAt { get; set; }
}
