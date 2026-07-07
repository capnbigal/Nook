using Nook.Models;
using Nook.Services;
using Xunit;

namespace Nook.Tests;

public class NodeSaveBodyTests
{
    [Fact]
    public async Task SaveBody_sets_body_and_preserves_tags()
    {
        var h = new GraphHarness();
        var svc = h.Nodes("u");
        var tag = await h.Tags("u").CreateAsync("friends");
        var node = await svc.CreateAsync(new Node { Title = "Jamie" }, new[] { tag.TagId });
        var t0 = (await svc.GetByIdAsync(node.NodeId))!.UpdatedAt;

        await svc.SaveBodyAsync(node.NodeId, "new body [[Link]]");

        var reloaded = await svc.GetByIdAsync(node.NodeId);
        Assert.Equal("new body [[Link]]", reloaded!.Body);
        Assert.Single(reloaded.Tags);                 // tags NOT cleared
        Assert.Equal("Jamie", reloaded.Title);         // title untouched
        Assert.True(reloaded.UpdatedAt >= t0);         // UpdatedAt bumped
    }

    [Fact]
    public async Task SaveBody_is_user_scoped_no_op_for_other_user()
    {
        var h = new GraphHarness();
        var owned = await h.Nodes("a").CreateAsync(new Node { Title = "secret", Body = "original" });

        await h.Nodes("b").SaveBodyAsync(owned.NodeId, "hacked");

        var reloaded = await h.Nodes("a").GetByIdAsync(owned.NodeId);
        Assert.Equal("original", reloaded!.Body);      // untouched by other user
    }
}
