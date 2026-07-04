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

        var nodes = await db.Nodes
            .Where(n => n.UserId == userId)
            .Select(n => new { n.Kind, n.CreatedAt, TagCount = n.NodeTags.Count })
            .ToListAsync();

        var actions = await db.ActionItems
            .Where(a => a.UserId == userId)
            .Select(a => new { a.Status, a.Priority, a.DueDate, a.CompletedAt })
            .ToListAsync();

        var now = DateTime.UtcNow;
        int totalNodes = nodes.Count;
        int completed = actions.Count(a => a.Status == ActionStatus.Done);
        int open = actions.Count(a => a.Status is ActionStatus.Open or ActionStatus.InProgress);
        int overdue = actions.Count(a => a.Status != ActionStatus.Done && a.Status != ActionStatus.Cancelled
                                          && a.DueDate != null && a.DueDate < now);
        int untagged = nodes.Count(n => n.TagCount == 0);
        int totalActions = actions.Count;
        double completionRate = totalActions == 0 ? 0 : Math.Round(completed * 100.0 / totalActions, 1);

        var byType = nodes.GroupBy(n => n.Kind)
            .Select(g => new CountSlice(g.Key.ToString(), g.Count()))
            .OrderByDescending(s => s.Count).ToList();
        var byStatus = actions.GroupBy(a => a.Status)
            .Select(g => new CountSlice(g.Key.ToString(), g.Count()))
            .OrderByDescending(s => s.Count).ToList();
        var byPriority = actions.Where(a => a.Priority != null)
            .GroupBy(a => a.Priority!.Value)
            .Select(g => new CountSlice(g.Key.ToString(), g.Count()))
            .OrderByDescending(s => s.Count).ToList();

        var topTags = await db.Tags
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.NodeTags.Count(nt => nt.Node.UserId == userId))
            .ThenBy(t => t.Name)
            .Take(10)
            .Select(t => new CountSlice(t.Name, t.NodeTags.Count(nt => nt.Node.UserId == userId)))
            .ToListAsync();

        DayOfWeek? busiest = nodes.Count == 0 ? null :
            nodes.GroupBy(n => n.CreatedAt.DayOfWeek)
                 .OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
                 .First().Key;

        var weekly = BuildWeeklyTrend(
            nodes.Select(n => n.CreatedAt).ToList(),
            actions.Where(a => a.CompletedAt != null).Select(a => a.CompletedAt!.Value).ToList(),
            now, weeks: 8);

        return new AnalyticsModel(
            totalNodes, open, completed, completionRate, overdue, untagged, busiest,
            byType, byStatus, byPriority, topTags, weekly);
    }

    private static List<WeekPoint> BuildWeeklyTrend(
        List<DateTime> created, List<DateTime> completed, DateTime now, int weeks)
    {
        var points = new List<WeekPoint>();
        var today = DateOnly.FromDateTime(now);
        int offset = ((int)today.DayOfWeek + 6) % 7; // Monday=0
        var currentMonday = today.AddDays(-offset);

        for (int w = weeks - 1; w >= 0; w--)
        {
            var start = currentMonday.AddDays(-7 * w);
            var end = start.AddDays(7);
            int c = created.Count(d => { var o = DateOnly.FromDateTime(d); return o >= start && o < end; });
            int done = completed.Count(d => { var o = DateOnly.FromDateTime(d); return o >= start && o < end; });
            points.Add(new WeekPoint(start, c, done));
        }
        return points;
    }
}
