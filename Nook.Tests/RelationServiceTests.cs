using Nook.Models;
using Nook.Services;
using Xunit;

namespace Nook.Tests;

public class RelationServiceTests
{
    private static async Task<(GraphHarness h, RelationType related, RelationType worksOn)> SetupAsync()
    {
        var h = new GraphHarness();
        await h.Migration().SeedSystemDataAsync();
        var types = await h.Relations("u").GetRelationTypesAsync();
        return (h, types.First(t => t.Name == "related to"), types.First(t => t.Name == "works on"));
    }

    [Fact]
    public async Task Rejects_self_link()
    {
        var (h, related, _) = await SetupAsync();
        var a = await h.Nodes("u").CreateAsync(new Node { Title = "a" });
        var result = await h.Relations("u").AddRelationAsync(a.NodeId, a.NodeId, related.RelationTypeId);
        Assert.Equal(AddRelationResult.SelfLink, result);
    }

    [Fact]
    public async Task Prevents_duplicate_relations()
    {
        var (h, related, _) = await SetupAsync();
        var a = await h.Nodes("u").CreateAsync(new Node { Title = "a" });
        var b = await h.Nodes("u").CreateAsync(new Node { Title = "b" });
        var rel = h.Relations("u");

        Assert.Equal(AddRelationResult.Added, await rel.AddRelationAsync(a.NodeId, b.NodeId, related.RelationTypeId));
        Assert.Equal(AddRelationResult.Duplicate, await rel.AddRelationAsync(a.NodeId, b.NodeId, related.RelationTypeId));
    }

    [Fact]
    public async Task Symmetric_relation_is_canonicalised_and_deduped_both_directions()
    {
        var (h, related, _) = await SetupAsync();
        var a = await h.Nodes("u").CreateAsync(new Node { Title = "a" });
        var b = await h.Nodes("u").CreateAsync(new Node { Title = "b" });
        var rel = h.Relations("u");

        Assert.Equal(AddRelationResult.Added, await rel.AddRelationAsync(b.NodeId, a.NodeId, related.RelationTypeId));
        // Reverse direction of a symmetric type must be a duplicate.
        Assert.Equal(AddRelationResult.Duplicate, await rel.AddRelationAsync(a.NodeId, b.NodeId, related.RelationTypeId));

        // It appears as a single connection on each node.
        Assert.True((await rel.GetConnectionsAsync(a.NodeId)).Any);
        Assert.True((await rel.GetConnectionsAsync(b.NodeId)).Any);
    }

    [Fact]
    public async Task Directional_relation_shows_inverse_label_on_backlink()
    {
        var (h, _, worksOn) = await SetupAsync();
        var person = await h.Nodes("u").CreateAsync(new Node { Title = "Jamie", Kind = NodeKind.Person });
        var project = await h.Nodes("u").CreateAsync(new Node { Title = "Project X", Kind = NodeKind.Project });
        var rel = h.Relations("u");
        await rel.AddRelationAsync(person.NodeId, project.NodeId, worksOn.RelationTypeId);

        var outgoing = await rel.GetConnectionsAsync(person.NodeId);
        var backlink = await rel.GetConnectionsAsync(project.NodeId);

        Assert.Equal("works on", outgoing.Outgoing.Single().Label);
        Assert.Equal("worked on by", backlink.Backlinks.Single().Label);
    }

    [Fact]
    public async Task Cannot_relate_another_users_node()
    {
        var (h, related, _) = await SetupAsync();
        var mine = await h.Nodes("u").CreateAsync(new Node { Title = "mine" });
        var theirs = await h.Nodes("other").CreateAsync(new Node { Title = "theirs" });

        var result = await h.Relations("u").AddRelationAsync(mine.NodeId, theirs.NodeId, related.RelationTypeId);

        Assert.Equal(AddRelationResult.InvalidNodes, result);
    }
}
