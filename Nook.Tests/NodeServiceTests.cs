using Nook.Models;
using Nook.Services;
using Xunit;

namespace Nook.Tests;

public class NodeServiceTests
{
    [Fact]
    public async Task QuickCapture_defaults_to_Unclassified_and_Inbox()
    {
        var h = new GraphHarness();
        var svc = h.Nodes("u");

        var node = await svc.QuickCaptureAsync("Remind friend to pick me up");

        Assert.Equal(NodeKind.Unclassified, node.Kind);
        Assert.Equal(NodeState.Inbox, node.State);
        Assert.Equal("u", node.UserId);
        Assert.True(node.NodeId > 0);
    }

    [Fact]
    public async Task Promote_preserves_identity_and_data()
    {
        var h = new GraphHarness();
        var svc = h.Nodes("u");
        var tag = await h.Tags("u").CreateAsync("friends");
        var node = await svc.CreateAsync(new Node { Title = "Jamie" }, new[] { tag.TagId });
        var originalId = node.NodeId;

        await svc.PromoteAsync(originalId, NodeKind.Person);

        var reloaded = await svc.GetByIdAsync(originalId);
        Assert.NotNull(reloaded);
        Assert.Equal(originalId, reloaded!.NodeId);          // same identity
        Assert.Equal(NodeKind.Person, reloaded.Kind);
        Assert.Equal(NodeState.Active, reloaded.State);       // promoted out of inbox
        Assert.Single(reloaded.Tags);                         // tags preserved
    }

    [Fact]
    public async Task Query_excludes_other_users_nodes()
    {
        var h = new GraphHarness();
        await h.Nodes("a").CreateAsync(new Node { Title = "A's node", State = NodeState.Active });
        await h.Nodes("b").CreateAsync(new Node { Title = "B's node", State = NodeState.Active });

        var aNodes = await h.Nodes("a").QueryAsync(new NodeFilter());
        var bNodes = await h.Nodes("b").QueryAsync(new NodeFilter());

        Assert.Single(aNodes);
        Assert.Equal("A's node", aNodes[0].Title);
        Assert.Single(bNodes);
    }

    [Fact]
    public async Task Archive_hides_from_default_query_but_keeps_node()
    {
        var h = new GraphHarness();
        var svc = h.Nodes("u");
        var node = await svc.CreateAsync(new Node { Title = "keep", State = NodeState.Active });

        await svc.ArchiveAsync(node.NodeId);

        Assert.Empty(await svc.QueryAsync(new NodeFilter()));
        Assert.Single(await svc.QueryAsync(new NodeFilter { ArchivedOnly = true }));
        var reloaded = await svc.GetByIdAsync(node.NodeId);
        Assert.Equal(NodeState.Archived, reloaded!.State);
    }

    [Fact]
    public async Task Unassigned_filter_includes_unclassified_inbox_and_uncollected()
    {
        var h = new GraphHarness();
        var svc = h.Nodes("u");
        await svc.QuickCaptureAsync("inbox unclassified");   // matches
        var organised = await svc.CreateAsync(new Node
        {
            Title = "organised", Kind = NodeKind.Note, State = NodeState.Active
        });
        var col = await h.Collections("u").CreateAsync("Folder", CollectionKind.Folder);
        await h.Collections("u").AddMemberAsync(col.NodeId, organised.NodeId);

        var unassigned = await svc.QueryAsync(new NodeFilter { UnassignedOnly = true });

        Assert.Contains(unassigned, n => n.Title == "inbox unclassified");
        Assert.DoesNotContain(unassigned, n => n.Title == "organised");
    }

    [Fact]
    public async Task Delete_removes_node_and_its_relations()
    {
        var h = new GraphHarness();
        var svc = h.Nodes("u");
        var a = await svc.CreateAsync(new Node { Title = "a" });
        var b = await svc.CreateAsync(new Node { Title = "b" });
        var relSvc = h.Relations("u");
        var rt = (await relSvc.GetRelationTypesAsync()); // empty until seeded
        var mig = h.Migration();
        await mig.SeedSystemDataAsync();
        var related = (await relSvc.GetRelationTypesAsync()).First(r => r.Name == "related to");
        await relSvc.AddRelationAsync(a.NodeId, b.NodeId, related.RelationTypeId);

        await svc.DeleteAsync(a.NodeId);

        Assert.Null(await svc.GetByIdAsync(a.NodeId));
        var conns = await relSvc.GetConnectionsAsync(b.NodeId);
        Assert.False(conns.Any);
    }
}
