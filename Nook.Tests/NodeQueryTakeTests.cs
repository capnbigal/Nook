using Nook.Models;
using Nook.Services;
using Xunit;

namespace Nook.Tests;

public class NodeQueryTakeTests
{
    [Fact]
    public async Task Take_caps_result_count_after_ordering()
    {
        var h = new GraphHarness();
        var svc = h.Nodes("u");
        for (var i = 0; i < 5; i++)
            await svc.CreateAsync(new Node { Title = $"n{i}", State = NodeState.Active });

        var capped = await svc.QueryAsync(new NodeFilter { Take = 3 });
        var all = await svc.QueryAsync(new NodeFilter());

        Assert.Equal(3, capped.Count);
        Assert.Equal(5, all.Count); // null Take = unbounded
    }

    [Fact]
    public async Task Take_keeps_the_most_recently_updated_first()
    {
        var h = new GraphHarness();
        var svc = h.Nodes("u");
        var first = await svc.CreateAsync(new Node { Title = "oldest", State = NodeState.Active });
        await svc.CreateAsync(new Node { Title = "newest", State = NodeState.Active });

        var top1 = await svc.QueryAsync(new NodeFilter { Take = 1 });

        Assert.Single(top1);
        Assert.NotEqual(first.NodeId, top1[0].NodeId); // newest survives the cap
    }
}
