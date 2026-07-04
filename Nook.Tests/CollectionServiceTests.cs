using Nook.Models;
using Nook.Services;
using Xunit;

namespace Nook.Tests;

public class CollectionServiceTests
{
    [Fact]
    public async Task Create_makes_a_node_backed_collection()
    {
        var h = new GraphHarness();
        var node = await h.Collections("u").CreateAsync("Movies to watch with Jamie", CollectionKind.Queue);

        Assert.Equal(NodeKind.Collection, node.Kind);
        var col = await h.Collections("u").GetAsync(node.NodeId);
        Assert.NotNull(col);
        Assert.Equal(CollectionKind.Queue, col!.Kind);
        Assert.True(col.IsOrdered);
    }

    [Fact]
    public async Task Node_can_belong_to_multiple_collections_without_duplication()
    {
        var h = new GraphHarness();
        var resource = await h.Nodes("u").CreateAsync(new Node { Title = "A great article", Kind = NodeKind.Resource });
        var reading = await h.Collections("u").CreateAsync("Reading list", CollectionKind.List);
        var projectRefs = await h.Collections("u").CreateAsync("Project X refs", CollectionKind.Folder);

        Assert.True(await h.Collections("u").AddMemberAsync(reading.NodeId, resource.NodeId));
        Assert.True(await h.Collections("u").AddMemberAsync(projectRefs.NodeId, resource.NodeId));
        Assert.False(await h.Collections("u").AddMemberAsync(reading.NodeId, resource.NodeId)); // dup ignored

        var memberships = await h.Collections("u").GetCollectionsForNodeAsync(resource.NodeId);
        Assert.Equal(2, memberships.Count);
    }

    [Fact]
    public async Task Move_reorders_queue_members()
    {
        var h = new GraphHarness();
        var q = await h.Collections("u").CreateAsync("Queue", CollectionKind.Queue);
        var one = await h.Nodes("u").CreateAsync(new Node { Title = "one" });
        var two = await h.Nodes("u").CreateAsync(new Node { Title = "two" });
        var three = await h.Nodes("u").CreateAsync(new Node { Title = "three" });
        var col = h.Collections("u");
        await col.AddMemberAsync(q.NodeId, one.NodeId);
        await col.AddMemberAsync(q.NodeId, two.NodeId);
        await col.AddMemberAsync(q.NodeId, three.NodeId);

        await col.MoveMemberAsync(q.NodeId, three.NodeId, up: true); // three moves above two

        var members = await col.GetMembersAsync(q.NodeId);
        Assert.Equal(new[] { "one", "three", "two" }, members.Select(m => m.Title).ToArray());
    }

    [Fact]
    public async Task Cannot_add_another_users_node_to_collection()
    {
        var h = new GraphHarness();
        var q = await h.Collections("u").CreateAsync("Queue", CollectionKind.Queue);
        var theirs = await h.Nodes("other").CreateAsync(new Node { Title = "theirs" });

        var ok = await h.Collections("u").AddMemberAsync(q.NodeId, theirs.NodeId);

        Assert.False(ok);
    }

    [Fact]
    public async Task Collections_are_scoped_per_user()
    {
        var h = new GraphHarness();
        await h.Collections("a").CreateAsync("A's queue", CollectionKind.Queue);
        Assert.Single(await h.Collections("a").GetCollectionsAsync());
        Assert.Empty(await h.Collections("b").GetCollectionsAsync());
    }

    // ---- Inline create-and-assign (UX refinement) ----

    [Fact]
    public async Task CreateAndAddMember_creates_collection_and_membership_in_one_step()
    {
        var h = new GraphHarness();
        var col = h.Collections("u");
        var node = await h.Nodes("u").CreateAsync(new Node { Title = "A note", Kind = NodeKind.Note });

        var collectionNode = await col.CreateAndAddMemberAsync("Read later", CollectionKind.Queue, "stuff", node.NodeId);

        Assert.Equal(NodeKind.Collection, collectionNode.Kind);
        var summaries = await col.GetCollectionSummariesForNodeAsync(node.NodeId);
        Assert.Single(summaries);
        Assert.Equal("Read later", summaries[0].Node.Title);
        Assert.Equal(CollectionKind.Queue, summaries[0].Kind);
        Assert.True(summaries[0].IsOrdered);
    }

    [Fact]
    public async Task Draft_flow_applies_pending_existing_and_new_collections_after_node_save()
    {
        // Simulates the unsaved-node editor: the node is created first, then queued
        // collection selections (one existing, one brand new) are applied.
        var h = new GraphHarness();
        var col = h.Collections("u");
        var existing = await col.CreateAsync("Existing folder", CollectionKind.Folder);

        // Node "saved":
        var node = await h.Nodes("u").CreateAsync(new Node { Title = "Draft note", Kind = NodeKind.Note });

        // Pending applied:
        Assert.True(await col.AddMemberAsync(existing.NodeId, node.NodeId));
        await col.CreateAndAddMemberAsync("Fresh queue", CollectionKind.Queue, null, node.NodeId);

        var memberships = await col.GetCollectionSummariesForNodeAsync(node.NodeId);
        Assert.Equal(2, memberships.Count);
        Assert.Contains(memberships, m => m.Node.Title == "Existing folder");
        Assert.Contains(memberships, m => m.Node.Title == "Fresh queue");
    }

    [Fact]
    public async Task Duplicate_collection_name_is_rejected()
    {
        var h = new GraphHarness();
        var col = h.Collections("u");
        await col.CreateAsync("Reading", CollectionKind.List);

        Assert.True(await col.NameExistsAsync("reading")); // case-insensitive
        await Assert.ThrowsAsync<InvalidOperationException>(() => col.CreateAsync("Reading", CollectionKind.Folder));
        var node = await h.Nodes("u").CreateAsync(new Node { Title = "n" });
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => col.CreateAndAddMemberAsync("reading", CollectionKind.Queue, null, node.NodeId));
    }

    [Fact]
    public async Task CreateAndAddMember_rejects_another_users_node()
    {
        var h = new GraphHarness();
        var theirs = await h.Nodes("other").CreateAsync(new Node { Title = "theirs" });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => h.Collections("u").CreateAndAddMemberAsync("Mine", CollectionKind.Folder, null, theirs.NodeId));
    }

    [Fact]
    public async Task Removing_membership_keeps_both_node_and_collection()
    {
        var h = new GraphHarness();
        var col = h.Collections("u");
        var node = await h.Nodes("u").CreateAsync(new Node { Title = "keep me", Kind = NodeKind.Note });
        var collectionNode = await col.CreateAndAddMemberAsync("Box", CollectionKind.Folder, null, node.NodeId);

        await col.RemoveMemberAsync(collectionNode.NodeId, node.NodeId);

        Assert.Empty(await col.GetCollectionSummariesForNodeAsync(node.NodeId));
        Assert.NotNull(await h.Nodes("u").GetByIdAsync(node.NodeId));        // node survives
        Assert.NotNull(await col.GetAsync(collectionNode.NodeId));           // collection survives
    }

    [Fact]
    public async Task Queue_ordering_stays_valid_after_add_and_remove()
    {
        var h = new GraphHarness();
        var col = h.Collections("u");
        var q = await col.CreateAsync("Q", CollectionKind.Queue);
        var a = await h.Nodes("u").CreateAsync(new Node { Title = "a" });
        var b = await h.Nodes("u").CreateAsync(new Node { Title = "b" });
        var c = await h.Nodes("u").CreateAsync(new Node { Title = "c" });
        await col.AddMemberAsync(q.NodeId, a.NodeId);
        await col.AddMemberAsync(q.NodeId, b.NodeId);
        await col.AddMemberAsync(q.NodeId, c.NodeId);

        await col.RemoveMemberAsync(q.NodeId, b.NodeId);           // remove the middle
        var d = await h.Nodes("u").CreateAsync(new Node { Title = "d" });
        await col.AddMemberAsync(q.NodeId, d.NodeId);              // append
        await col.MoveMemberAsync(q.NodeId, d.NodeId, up: true);   // d above c

        var members = await col.GetMembersAsync(q.NodeId);
        Assert.Equal(new[] { "a", "d", "c" }, members.Select(m => m.Title).ToArray());
    }
}
