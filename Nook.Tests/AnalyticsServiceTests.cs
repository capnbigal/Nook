using Nook.Models;
using Nook.Services;
using Xunit;

namespace Nook.Tests;

public class AnalyticsServiceTests
{
    private static async Task SeedAsync(TestDbContextFactory factory)
    {
        await using var db = factory.CreateDbContext();
        db.Items.AddRange(
            new Item { Title = "n1", ItemType = ItemType.Note, Status = ItemStatus.Open, UserId = "u" },
            new Item { Title = "t1", ItemType = ItemType.Todo, Status = ItemStatus.Done, UserId = "u",
                       CompletedDate = DateTime.UtcNow },
            new Item { Title = "t2", ItemType = ItemType.Todo, Status = ItemStatus.Done, UserId = "u",
                       CompletedDate = DateTime.UtcNow },
            // Another user's item must be ignored.
            new Item { Title = "x", ItemType = ItemType.Note, Status = ItemStatus.Open, UserId = "other" });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetForUserAsync_counts_only_that_users_items()
    {
        var factory = new TestDbContextFactory();
        await SeedAsync(factory);
        var sut = new AnalyticsService(factory);

        var model = await sut.GetForUserAsync("u");

        Assert.Equal(3, model.TotalItems);
        Assert.Equal(2, model.CompletedItems);
        Assert.Equal(1, model.OpenItems);
    }

    [Fact]
    public async Task GetForUserAsync_computes_completion_rate()
    {
        var factory = new TestDbContextFactory();
        await SeedAsync(factory);
        var sut = new AnalyticsService(factory);

        var model = await sut.GetForUserAsync("u");

        // 2 of 3 completed ≈ 66.7%.
        Assert.True(model.CompletionRatePercent > 66 && model.CompletionRatePercent < 67);
    }
}
