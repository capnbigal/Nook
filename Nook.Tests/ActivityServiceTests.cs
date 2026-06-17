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
        await sut.LogAsync("user-a", ActivityType.Completed, 1, "First", "done");
        await sut.LogAsync("user-b", ActivityType.Created, 2, "Other", null);

        var rows = await sut.GetForUserAsync("user-a");

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal("user-a", r.UserId));
        // Newest first.
        Assert.True(rows[0].Timestamp >= rows[1].Timestamp);
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
