using Nook.Models;
using Nook.Services;
using Xunit;

namespace Nook.Tests;

public class TagNodeTests
{
    [Fact]
    public async Task Assign_and_remove_tag_on_node_and_summary_counts_nodes()
    {
        var h = new GraphHarness();
        var tags = h.Tags("u");
        var tag = await tags.CreateAsync("work", "#1E88E5");
        var node = await h.Nodes("u").CreateAsync(new Node { Title = "task", Kind = NodeKind.Note });

        await tags.AssignToNodeAsync(node.NodeId, tag.TagId);
        var summary = await tags.GetTagSummaryAsync();
        Assert.Equal(1, summary.Single(s => s.TagId == tag.TagId).ItemCount);

        await tags.RemoveFromNodeAsync(node.NodeId, tag.TagId);
        summary = await tags.GetTagSummaryAsync();
        Assert.Equal(0, summary.Single(s => s.TagId == tag.TagId).ItemCount);
    }

    [Fact]
    public async Task Cannot_assign_another_users_tag_or_node()
    {
        var h = new GraphHarness();
        var myTag = await h.Tags("u").CreateAsync("mine");
        var theirNode = await h.Nodes("other").CreateAsync(new Node { Title = "theirs" });

        await h.Tags("u").AssignToNodeAsync(theirNode.NodeId, myTag.TagId);

        var summary = await h.Tags("u").GetTagSummaryAsync();
        Assert.Equal(0, summary.Single(s => s.TagId == myTag.TagId).ItemCount);
    }
}
