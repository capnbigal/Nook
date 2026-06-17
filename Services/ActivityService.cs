using Microsoft.EntityFrameworkCore;
using Nook.Data;
using Nook.Models;

namespace Nook.Services;

public sealed class ActivityService : IActivityService
{
    private readonly IDbContextFactory<NookContext> _factory;

    public ActivityService(IDbContextFactory<NookContext> factory)
    {
        _factory = factory;
    }

    public async Task LogAsync(string userId, ActivityType type, int? itemId, string itemTitle, string? detail = null)
    {
        await using var db = await _factory.CreateDbContextAsync();
        db.ActivityLogs.Add(new ActivityLog
        {
            UserId = userId,
            Type = type,
            ItemId = itemId,
            ItemTitle = itemTitle.Length > 300 ? itemTitle[..300] : itemTitle,
            Detail = detail,
            Timestamp = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    public async Task<List<ActivityLog>> GetForUserAsync(
        string userId, ActivityType? type = null, DateTime? from = null, DateTime? to = null, int? take = null)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var query = db.ActivityLogs.Where(a => a.UserId == userId);

        if (type.HasValue) query = query.Where(a => a.Type == type.Value);
        if (from.HasValue) query = query.Where(a => a.Timestamp >= from.Value);
        if (to.HasValue) query = query.Where(a => a.Timestamp <= to.Value);

        query = query.OrderByDescending(a => a.Timestamp).ThenByDescending(a => a.ActivityLogId);
        if (take.HasValue) query = query.Take(take.Value);

        return await query.ToListAsync();
    }
}
