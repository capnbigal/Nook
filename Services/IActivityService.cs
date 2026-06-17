using Nook.Models;

namespace Nook.Services;

/// <summary>Writes and queries the activity audit log.</summary>
public interface IActivityService
{
    Task LogAsync(string userId, ActivityType type, int? itemId, string itemTitle, string? detail = null);

    Task<List<ActivityLog>> GetForUserAsync(
        string userId, ActivityType? type = null, DateTime? from = null, DateTime? to = null, int? take = null);
}
