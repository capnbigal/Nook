using Nook.Models;
using Nook.Services;
using Xunit;

namespace Nook.Tests;

public class ItemServiceScopingTests
{
    private static (ItemService svc, IActivityService activity, TestDbContextFactory factory)
        MakeService(string userId)
    {
        var factory = new TestDbContextFactory();
        var activity = new ActivityService(factory);
        var svc = new ItemService(factory, new FakeCurrentUser(userId), activity);
        return (svc, activity, factory);
    }

    [Fact]
    public async Task GetItemsAsync_returns_only_current_users_items()
    {
        var (svcA, _, factory) = MakeService("user-a");
        await svcA.CreateAsync(new Item { Title = "A's note", ItemType = ItemType.Note });

        var svcB = new ItemService(factory, new FakeCurrentUser("user-b"), new ActivityService(factory));
        await svcB.CreateAsync(new Item { Title = "B's note", ItemType = ItemType.Note });

        var aItems = await svcA.GetItemsAsync(new ItemFilter());
        var bItems = await svcB.GetItemsAsync(new ItemFilter());

        Assert.Single(aItems);
        Assert.Equal("A's note", aItems[0].Title);
        Assert.Single(bItems);
        Assert.Equal("B's note", bItems[0].Title);
    }

    [Fact]
    public async Task CreateAsync_stamps_userId_and_logs_Created()
    {
        var (svc, activity, _) = MakeService("user-a");

        var item = await svc.CreateAsync(new Item { Title = "New", ItemType = ItemType.Note });

        Assert.Equal("user-a", item.UserId);
        var log = await activity.GetForUserAsync("user-a");
        Assert.Single(log);
        Assert.Equal(ActivityType.Created, log[0].Type);
        Assert.Equal(item.ItemId, log[0].ItemId);
    }

    [Fact]
    public async Task CompleteAsync_logs_Completed_and_ignores_other_users_items()
    {
        var (svcA, activityA, factory) = MakeService("user-a");
        var item = await svcA.CreateAsync(new Item { Title = "Todo", ItemType = ItemType.Todo });

        // user-b cannot complete user-a's item.
        var svcB = new ItemService(factory, new FakeCurrentUser("user-b"), new ActivityService(factory));
        await svcB.CompleteAsync(item.ItemId);
        var afterB = await svcA.GetByIdAsync(item.ItemId);
        Assert.NotEqual(ItemStatus.Done, afterB!.Status);

        // user-a can.
        await svcA.CompleteAsync(item.ItemId);
        var afterA = await svcA.GetByIdAsync(item.ItemId);
        Assert.Equal(ItemStatus.Done, afterA!.Status);

        var completedLogs = await activityA.GetForUserAsync("user-a", ActivityType.Completed);
        Assert.Single(completedLogs);
    }

    [Fact]
    public async Task UnlinkAsync_ignores_other_users_links()
    {
        var (svcA, _, factory) = MakeService("user-a");
        var one = await svcA.CreateAsync(new Item { Title = "One", ItemType = ItemType.Note });
        var two = await svcA.CreateAsync(new Item { Title = "Two", ItemType = ItemType.Note });
        await svcA.LinkAsync(one.ItemId, two.ItemId);

        var withLink = await svcA.GetByIdAsync(one.ItemId);
        var linkId = withLink!.OutgoingLinks.Single().ItemLinkId;

        // user-b cannot remove user-a's link.
        var svcB = new ItemService(factory, new FakeCurrentUser("user-b"), new ActivityService(factory));
        await svcB.UnlinkAsync(linkId);
        var afterB = await svcA.GetByIdAsync(one.ItemId);
        Assert.Single(afterB!.OutgoingLinks);

        // user-a can.
        await svcA.UnlinkAsync(linkId);
        var afterA = await svcA.GetByIdAsync(one.ItemId);
        Assert.Empty(afterA!.OutgoingLinks);
    }
}
