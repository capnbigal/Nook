using Nook.Models;
using Nook.Services;
using Xunit;

namespace Nook.Tests;

public class ActivityServiceTests
{
    [Fact]
    public async Task LogAsync_then_GetForUser_returns_only_that_users_rows_newest_first()
    {
        var factory = new TestDbContextFactory();
        var sut = new ActivityService(factory);

        await sut.LogAsync("user-a", ActivityType.Created, 1, "First", null);
        // Ensure a distinct UtcNow so the assertion verifies real timestamp
        // ordering rather than leaning on the ActivityLogId tiebreak.
        await Task.Delay(20);
        await sut.LogAsync("user-a", ActivityType.Completed, 1, "First", "done");
        await sut.LogAsync("user-b", ActivityType.Created, 2, "Other", null);

        var rows = await sut.GetForUserAsync("user-a");

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal("user-a", r.UserId));
        // Newest first: the Completed row was logged after the Created row.
        Assert.True(rows[0].Timestamp > rows[1].Timestamp);
        Assert.Equal(ActivityType.Completed, rows[0].Type);
        Assert.Equal(ActivityType.Created, rows[1].Type);
    }

    [Fact]
    public async Task LogAsync_truncates_ItemTitle_longer_than_300_chars()
    {
        var factory = new TestDbContextFactory();
        var sut = new ActivityService(factory);

        var longTitle = new string('x', 400);
        await sut.LogAsync("u", ActivityType.Created, 1, longTitle, null);

        var rows = await sut.GetForUserAsync("u");

        Assert.Single(rows);
        Assert.Equal(300, rows[0].ItemTitle.Length);
    }

    [Fact]
    public async Task GetForUserAsync_filters_by_type()
    {
        var factory = new TestDbContextFactory();
        var sut = new ActivityService(factory);

        await sut.LogAsync("u", ActivityType.Created, 1, "X", null);
        await sut.LogAsync("u", ActivityType.Completed, 1, "X", null);

        var completed = await sut.GetForUserAsync("u", type: ActivityType.Completed);

        Assert.Single(completed);
        Assert.Equal(ActivityType.Completed, completed[0].Type);
    }
}
