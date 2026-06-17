using Nook.Models;
using Nook.Services;
using Xunit;

namespace Nook.Tests;

public class TimelineServiceTests
{
    private static ActivityLog Log(ActivityType type, DateTime ts) =>
        new() { UserId = "u", Type = type, ItemTitle = "x", Timestamp = ts };

    [Fact]
    public void GenerateWeekShoutouts_counts_completed_todos()
    {
        var monday = new DateTime(2026, 6, 8, 9, 0, 0, DateTimeKind.Utc);
        var events = new List<ActivityLog>
        {
            Log(ActivityType.Completed, monday),
            Log(ActivityType.Completed, monday.AddHours(2)),
            Log(ActivityType.Created, monday.AddDays(1)),
        };

        var shoutouts = TimelineService.GenerateWeekShoutouts(events);

        Assert.Contains(shoutouts, s => s.Text.Contains("2") && s.Text.Contains("completed"));
    }

    [Fact]
    public void GenerateWeekShoutouts_reports_busiest_day()
    {
        var monday = new DateTime(2026, 6, 8, 9, 0, 0, DateTimeKind.Utc);
        var tuesday = monday.AddDays(1);
        var events = new List<ActivityLog>
        {
            Log(ActivityType.Created, monday),
            Log(ActivityType.Created, tuesday),
            Log(ActivityType.Created, tuesday.AddHours(1)),
            Log(ActivityType.Created, tuesday.AddHours(2)),
        };

        var shoutouts = TimelineService.GenerateWeekShoutouts(events);

        Assert.Contains(shoutouts, s => s.Text.Contains("Tuesday"));
    }

    [Fact]
    public void GenerateWeekShoutouts_returns_empty_for_no_events()
    {
        var shoutouts = TimelineService.GenerateWeekShoutouts(new List<ActivityLog>());
        Assert.Empty(shoutouts);
    }
}
