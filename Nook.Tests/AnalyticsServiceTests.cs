using Nook.Models;
using Nook.Services;
using Xunit;

namespace Nook.Tests;

public class AnalyticsServiceTests
{
    private static async Task SeedAsync(TestDbContextFactory factory)
    {
        await using var db = factory.CreateDbContext();
        // Three nodes for user "u" (TotalItems counts nodes), one for another user.
        db.Nodes.AddRange(
            new Node { Title = "n1", Kind = NodeKind.Note, State = NodeState.Active, UserId = "u" },
            new Node { Title = "n2", Kind = NodeKind.Idea, State = NodeState.Active, UserId = "u" },
            new Node { Title = "n3", Kind = NodeKind.Note, State = NodeState.Active, UserId = "u" },
            new Node { Title = "x", Kind = NodeKind.Note, State = NodeState.Active, UserId = "other" });
        // Actions drive completion: 2 done of 3 total for user "u".
        db.ActionItems.AddRange(
            new ActionItem { Title = "a1", Kind = ActionKind.Task, Status = ActionStatus.Open, UserId = "u" },
            new ActionItem { Title = "a2", Kind = ActionKind.Task, Status = ActionStatus.Done, UserId = "u",
                             CompletedAt = DateTime.UtcNow },
            new ActionItem { Title = "a3", Kind = ActionKind.Task, Status = ActionStatus.Done, UserId = "u",
                             CompletedAt = DateTime.UtcNow },
            new ActionItem { Title = "ax", Kind = ActionKind.Task, Status = ActionStatus.Done, UserId = "other",
                             CompletedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetForUserAsync_counts_only_that_users_nodes_and_actions()
    {
        var factory = new TestDbContextFactory();
        await SeedAsync(factory);
        var sut = new AnalyticsService(factory);

        var model = await sut.GetForUserAsync("u");

        Assert.Equal(3, model.TotalItems);      // nodes owned by "u"
        Assert.Equal(2, model.CompletedItems);  // done actions
        Assert.Equal(1, model.OpenItems);       // open actions
    }

    [Fact]
    public async Task GetForUserAsync_computes_completion_rate()
    {
        var factory = new TestDbContextFactory();
        await SeedAsync(factory);
        var sut = new AnalyticsService(factory);

        var model = await sut.GetForUserAsync("u");

        // 2 of 3 actions completed ≈ 66.7%.
        Assert.True(model.CompletionRatePercent > 66 && model.CompletionRatePercent < 67);
    }
}
