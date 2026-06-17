using Nook.Models;
using Nook.Services;
using Xunit;

namespace Nook.Tests;

public class TagServiceTests
{
    private static async Task<(int itemId, int tagId)> SeedItemAndTagAsync(
        TestDbContextFactory factory, string userId)
    {
        await using var db = factory.CreateDbContext();
        var item = new Item { Title = "Note", ItemType = ItemType.Note, UserId = userId };
        var tag = new Tag { Name = "work", UserId = userId };
        db.Items.Add(item);
        db.Tags.Add(tag);
        await db.SaveChangesAsync();
        return (item.ItemId, tag.TagId);
    }

    [Fact]
    public async Task AssignTagAsync_logs_a_Tagged_activity()
    {
        var factory = new TestDbContextFactory();
        var (itemId, tagId) = await SeedItemAndTagAsync(factory, "u");
        var activity = new ActivityService(factory);
        var sut = new TagService(factory, new FakeCurrentUser("u"), activity);

        await sut.AssignTagAsync(itemId, tagId);

        var logs = await activity.GetForUserAsync("u", ActivityType.Tagged);
        Assert.Single(logs);
        Assert.Equal(itemId, logs[0].ItemId);
        Assert.Contains("added tag 'work'", logs[0].Detail);
    }

    [Fact]
    public async Task RemoveTagAsync_logs_a_Tagged_activity()
    {
        var factory = new TestDbContextFactory();
        var (itemId, tagId) = await SeedItemAndTagAsync(factory, "u");
        var activity = new ActivityService(factory);
        var sut = new TagService(factory, new FakeCurrentUser("u"), activity);
        await sut.AssignTagAsync(itemId, tagId);

        await sut.RemoveTagAsync(itemId, tagId);

        var removed = (await activity.GetForUserAsync("u", ActivityType.Tagged))
            .Where(l => l.Detail != null && l.Detail.Contains("removed"))
            .ToList();
        Assert.Single(removed);
        Assert.Contains("removed tag 'work'", removed[0].Detail);
    }

    [Fact]
    public async Task AssignTagAsync_ignores_another_users_item()
    {
        var factory = new TestDbContextFactory();
        var (itemId, tagId) = await SeedItemAndTagAsync(factory, "owner");
        var activity = new ActivityService(factory);
        // A different user cannot tag the owner's item.
        var sut = new TagService(factory, new FakeCurrentUser("intruder"), activity);

        await sut.AssignTagAsync(itemId, tagId);

        Assert.Empty(await activity.GetForUserAsync("intruder"));
        await using var db = factory.CreateDbContext();
        Assert.False(db.ItemTags.Any());
    }
}
