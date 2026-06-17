using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Nook.Data;
using Nook.Models;

namespace Nook.Services;

public sealed class TimelineService : ITimelineService
{
    private readonly IDbContextFactory<NookContext> _factory;

    public TimelineService(IDbContextFactory<NookContext> factory)
    {
        _factory = factory;
    }

    public async Task<List<TimelineEntry>> BuildAsync(string userId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var events = await db.ActivityLogs
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.Timestamp)
            .Take(500)
            .ToListAsync();

        var entries = new List<TimelineEntry>();
        if (events.Count == 0) return entries;

        // Group by day (newest-first), and emit week shoutouts when crossing week boundaries.
        var byDay = events
            .GroupBy(e => DateOnly.FromDateTime(e.Timestamp.ToLocalTime()))
            .OrderByDescending(g => g.Key);

        int? currentWeek = null;
        var weekBuffer = new List<ActivityLog>();

        void FlushWeek()
        {
            if (weekBuffer.Count > 0)
            {
                foreach (var shoutout in GenerateWeekShoutouts(weekBuffer))
                    entries.Add(shoutout);
                weekBuffer.Clear();
            }
        }

        foreach (var day in byDay)
        {
            var week = IsoWeek(day.Key);
            if (currentWeek is not null && week != currentWeek)
            {
                FlushWeek();
            }
            currentWeek = week;
            weekBuffer.AddRange(day);
            entries.Add(new DayEntry(day.Key, day.OrderByDescending(e => e.Timestamp).ToList()));
        }
        FlushWeek();

        return entries;
    }

    private static int IsoWeek(DateOnly date) =>
        ISOWeek.GetWeekOfYear(date.ToDateTime(TimeOnly.MinValue)) + ISOWeek.GetYear(date.ToDateTime(TimeOnly.MinValue)) * 100;

    /// <summary>
    /// Deterministic shoutouts summarizing one week's events. Pure (no I/O) for testability.
    /// </summary>
    public static List<ShoutoutEntry> GenerateWeekShoutouts(IReadOnlyList<ActivityLog> weekEvents)
    {
        var shoutouts = new List<ShoutoutEntry>();
        if (weekEvents.Count == 0) return shoutouts;

        var completed = weekEvents.Count(e => e.Type == ActivityType.Completed);
        if (completed > 0)
        {
            shoutouts.Add(new ShoutoutEntry(
                $"{completed} item{(completed == 1 ? "" : "s")} completed this week 🎉",
                "Celebration"));
        }

        var created = weekEvents.Count(e => e.Type == ActivityType.Created);
        if (created > 0)
        {
            shoutouts.Add(new ShoutoutEntry(
                $"{created} new item{(created == 1 ? "" : "s")} captured",
                "NoteAdd"));
        }

        // Busiest day of the week.
        var busiest = weekEvents
            .GroupBy(e => e.Timestamp.ToLocalTime().DayOfWeek)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .First();
        if (busiest.Count() >= 2)
        {
            shoutouts.Add(new ShoutoutEntry(
                $"Most productive day: {busiest.Key} ({busiest.Count()} events)",
                "TrendingUp"));
        }

        return shoutouts;
    }
}
