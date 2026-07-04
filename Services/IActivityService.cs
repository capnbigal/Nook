using Nook.Models;

namespace Nook.Services;

/// <summary>Writes and queries the activity audit log.</summary>
public interface IActivityService
{
    Task LogAsync(string userId, ActivityType type, int? itemId, string itemTitle, string? detail = null);

    /// <summary>Records an audit entry against a graph node.</summary>
    Task LogNodeAsync(string userId, ActivityType type, int? nodeId, string title, string? detail = null);

    Task<List<ActivityLog>> GetForNodeAsync(string userId, int nodeId, int? take = null);

    Task<List<ActivityLog>> GetForUserAsync(
        string userId, ActivityType? type = null, DateTime? from = null, DateTime? to = null, int? take = null);
}
