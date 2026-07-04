using Nook.Models;
using Nook.Services;
using Xunit;

namespace Nook.Tests;

public class CryptexServiceTests
{
    [Fact]
    public async Task GetDataset_projects_facets_for_a_node()
    {
        var h = new GraphHarness();
        await h.Migration().SeedSystemDataAsync();
        var tag = await h.Tags("u").CreateAsync("work", "#1E88E5");
        var jamie = await h.Nodes("u").CreateAsync(new Node { Title = "Jamie", Kind = NodeKind.Person, State = NodeState.Active });
        var note = await h.Nodes("u").CreateAsync(new Node { Title = "Standup", Kind = NodeKind.Note, State = NodeState.Active }, new[] { tag.TagId });
        var coll = await h.Collections("u").CreateAsync("Project X", CollectionKind.Folder);
        await h.Collections("u").AddMemberAsync(coll.NodeId, note.NodeId);
        var assoc = (await h.Relations("u").GetRelationTypesAsync()).First(r => r.Name == "associated with");
        await h.Relations("u").AddRelationAsync(note.NodeId, jamie.NodeId, assoc.RelationTypeId);

        var data = await h.Cryptex("u").GetDatasetAsync();
        var cn = data.Single(d => d.NodeId == note.NodeId);

        Assert.Equal(NodeKind.Note, cn.Kind);
        Assert.Contains("work", cn.Tags);
        Assert.Contains("Project X", cn.Collections);
        Assert.Contains("Jamie", cn.People);
    }

    [Fact]
    public async Task GetDataset_excludes_other_users_nodes()
    {
        var h = new GraphHarness();
        await h.Nodes("a").CreateAsync(new Node { Title = "A's node", State = NodeState.Active });
        await h.Nodes("b").CreateAsync(new Node { Title = "B's node", State = NodeState.Active });

        Assert.Single(await h.Cryptex("a").GetDatasetAsync());
        Assert.Single(await h.Cryptex("b").GetDatasetAsync());
    }

    [Fact]
    public async Task GetDataset_includes_archived_so_the_state_wheel_can_reach_them()
    {
        var h = new GraphHarness();
        var n = await h.Nodes("u").CreateAsync(new Node { Title = "old", State = NodeState.Active });
        await h.Nodes("u").ArchiveAsync(n.NodeId);

        var data = await h.Cryptex("u").GetDatasetAsync();
        Assert.Single(data);
        Assert.Equal(NodeState.Archived, data[0].State);
    }

    [Fact]
    public async Task GetDataset_excludes_archived_collections_from_the_collection_facet()
    {
        var h = new GraphHarness();
        var node = await h.Nodes("u").CreateAsync(new Node { Title = "note", Kind = NodeKind.Note, State = NodeState.Active });
        var coll = await h.Collections("u").CreateAsync("Shelf", CollectionKind.Folder);
        await h.Collections("u").AddMemberAsync(coll.NodeId, node.NodeId);
        await h.Nodes("u").ArchiveAsync(coll.NodeId); // archive the collection node

        var data = await h.Cryptex("u").GetDatasetAsync();
        var cn = data.Single(d => d.NodeId == node.NodeId);
        Assert.Empty(cn.Collections); // archived collections don't appear in the facet
    }

    [Fact]
    public async Task AddNodeWithCode_stamps_kind_state_tag_collection_and_person()
    {
        var h = new GraphHarness();
        await h.Migration().SeedSystemDataAsync();
        var jamie = await h.Nodes("u").CreateAsync(new Node { Title = "Jamie", Kind = NodeKind.Person, State = NodeState.Active });
        var coll = await h.Collections("u").CreateAsync("Project X", CollectionKind.Folder);
        var code = new Dictionary<CryptexRing, string>
        {
            [CryptexRing.Kind] = "Idea", [CryptexRing.State] = "Active",
            [CryptexRing.Tag] = "spikes", [CryptexRing.Collection] = "Project X", [CryptexRing.People] = "Jamie",
        };

        var id = await h.Cryptex("u").AddNodeWithCodeAsync("Research spike", code);

        var node = await h.Nodes("u").GetByIdAsync(id);
        Assert.NotNull(node);
        Assert.Equal(NodeKind.Idea, node!.Kind);
        Assert.Equal(NodeState.Active, node.State);
        Assert.Contains(node.Tags, t => t.Name == "spikes");
        var dataset = await h.Cryptex("u").GetDatasetAsync();
        var cn = dataset.Single(d => d.NodeId == id);
        Assert.Contains("Project X", cn.Collections);
        Assert.Contains("Jamie", cn.People);
    }

    [Fact]
    public async Task AddNodeWithCode_defaults_to_unclassified_inbox_with_no_code()
    {
        var h = new GraphHarness();
        var id = await h.Cryptex("u").AddNodeWithCodeAsync("Loose thought", new Dictionary<CryptexRing, string>());

        var node = await h.Nodes("u").GetByIdAsync(id);
        Assert.Equal(NodeKind.Unclassified, node!.Kind);
        Assert.Equal(NodeState.Inbox, node.State);
    }

    [Fact]
    public async Task AddNodeWithCode_ignores_another_users_collection_but_still_creates_the_node()
    {
        var h = new GraphHarness();
        await h.Collections("other").CreateAsync("Theirs", CollectionKind.Folder);
        var code = new Dictionary<CryptexRing, string> { [CryptexRing.Collection] = "Theirs" };

        var id = await h.Cryptex("u").AddNodeWithCodeAsync("Mine", code);

        var dataset = await h.Cryptex("u").GetDatasetAsync();
        Assert.Empty(dataset.Single(d => d.NodeId == id).Collections); // not added to another user's collection
    }
}
