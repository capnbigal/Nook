using Microsoft.EntityFrameworkCore;
using Nook.Data;
using Nook.Models;

namespace Nook.Services;

public sealed class AnalyticsService : IAnalyticsService
{
    private readonly IDbContextFactory<NookContext> _factory;

    public AnalyticsService(IDbContextFactory<NookContext> factory)
    {
        _factory = factory;
    }

    public async Task<AnalyticsModel> GetForUserAsync(string userId)
    {
        await using var db = await _factory.CreateDbContextAsync();

        var items = await db.Items
            .Where(i => i.UserId == userId)
            .Select(i => new
            {
                i.ItemType, i.Status, i.Priority, i.DueDate, i.CreatedAt, i.CompletedDate,
                TagCount = i.ItemTags.Count
            })
            .ToListAsync();

        var total = items.Count;
        var completed = items.Count(i => i.Status == ItemStatus.Done);
        var open = items.Count(i => i.Status != ItemStatus.Done);
        var now = DateTime.UtcNow;
        var overdue = items.Count(i => i.Status != ItemStatus.Done && i.DueDate != null && i.DueDate < now);
        var untagged = items.Count(i => i.TagCount == 0);
        var completionRate = total == 0 ? 0 : Math.Round(completed * 100.0 / total, 1);

        var byType = items.GroupBy(i => i.ItemType)
            .Select(g => new CountSlice(g.Key.ToString(), g.Count()))
            .OrderByDescending(s => s.Count).ToList();
        var byStatus = items.GroupBy(i => i.Status)
            .Select(g => new CountSlice(g.Key.ToString(), g.Count()))
            .OrderByDescending(s => s.Count).ToList();
        var byPriority = items.Where(i => i.Priority != null)
            .GroupBy(i => i.Priority!.Value)
            .Select(g => new CountSlice(g.Key.ToString(), g.Count()))
            .OrderByDescending(s => s.Count).ToList();

        // Tag insights.
        // Count only links to items the user owns, so the figure can't be inflated
        // by a stray cross-user ItemTag even though tags are per-user today.
        var topTags = await db.Tags
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.ItemTags.Count(it => it.Item.UserId == userId))
            .ThenBy(t => t.Name)
            .Take(10)
            .Select(t => new CountSlice(t.Name, t.ItemTags.Count(it => it.Item.UserId == userId)))
            .ToListAsync();

        // Busiest day-of-week by item creation.
        DayOfWeek? busiest = items.Count == 0 ? null :
            items.GroupBy(i => i.CreatedAt.DayOfWeek)
                 .OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
                 .First().Key;

        // Weekly trend over the last 8 ISO weeks.
        var weekly = BuildWeeklyTrend(
            items.Select(i => (i.CreatedAt, i.CompletedDate)).ToList(), now, weeks: 8);

        return new AnalyticsModel(
            total, open, completed, completionRate, overdue, untagged, busiest,
            byType, byStatus, byPriority, topTags, weekly);
    }

    private static List<WeekPoint> BuildWeeklyTrend(
        List<(DateTime Created, DateTime? Completed)> items, DateTime now, int weeks)
    {
        var points = new List<WeekPoint>();
        // Monday of the current week.
        var today = DateOnly.FromDateTime(now);
        int offset = ((int)today.DayOfWeek + 6) % 7; // Monday=0
        var currentMonday = today.AddDays(-offset);

        for (int w = weeks - 1; w >= 0; w--)
        {
            var start = currentMonday.AddDays(-7 * w);
            var end = start.AddDays(7);
            var created = items.Count(i =>
            {
                var d = DateOnly.FromDateTime(i.Created);
                return d >= start && d < end;
            });
            var completed = items.Count(i =>
                i.Completed is DateTime c &&
                DateOnly.FromDateTime(c) >= start && DateOnly.FromDateTime(c) < end);
            points.Add(new WeekPoint(start, created, completed));
        }
        return points;
    }
}
