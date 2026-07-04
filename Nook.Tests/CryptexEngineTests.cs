using Nook.Models;
using Nook.Services;
using Xunit;

namespace Nook.Tests;

public class CryptexEngineTests
{
    private static CryptexNode N(int id, NodeKind kind, NodeState state,
        string[]? tags = null, string[]? colls = null, string[]? people = null) =>
        new(id, $"node{id}", kind, state, null,
            tags ?? System.Array.Empty<string>(),
            colls ?? System.Array.Empty<string>(),
            people ?? System.Array.Empty<string>());

    private static readonly List<CryptexNode> Data = new()
    {
        N(1, NodeKind.Idea,  NodeState.Active, tags: new[]{"work","ideas"}, colls: new[]{"Project X"}, people: new[]{"Jamie"}),
        N(2, NodeKind.Note,  NodeState.Inbox,  tags: new[]{"personal"},    colls: new[]{"Watchlist"}, people: new[]{"Jamie"}),
        N(3, NodeKind.Idea,  NodeState.Archived, tags: new[]{"ideas"}),
        N(4, NodeKind.Person, NodeState.Active, people: new[]{"Jamie"}),
    };

    [Fact]
    public void Empty_selection_matches_every_node()
    {
        var hits = CryptexEngine.Hits(Data, new Dictionary<CryptexRing,string>());
        Assert.Equal(4, hits.Count);
    }

    [Fact]
    public void Selecting_kind_and_tag_narrows_to_matching_nodes()
    {
        var sel = new Dictionary<CryptexRing,string> { [CryptexRing.Kind] = "Idea", [CryptexRing.Tag] = "ideas" };
        var hits = CryptexEngine.Hits(Data, sel);
        Assert.Equal(new[] { 1, 3 }, hits.Select(h => h.NodeId).OrderBy(x => x).ToArray());
    }

    [Fact]
    public void Selection_order_does_not_matter()
    {
        var a = new Dictionary<CryptexRing,string> { [CryptexRing.People] = "Jamie", [CryptexRing.State] = "Active" };
        var b = new Dictionary<CryptexRing,string> { [CryptexRing.State] = "Active", [CryptexRing.People] = "Jamie" };
        Assert.Equal(CryptexEngine.Hits(Data, a).Select(h => h.NodeId).OrderBy(x => x),
                     CryptexEngine.Hits(Data, b).Select(h => h.NodeId).OrderBy(x => x));
    }

    [Fact]
    public void Count_ignores_its_own_ring_so_the_selected_ring_still_shows_alternatives()
    {
        // With Kind=Idea selected, the Kind ring should still count Note nodes
        // (it ignores its own ring); the Tag ring should be constrained by Kind.
        var sel = new Dictionary<CryptexRing,string> { [CryptexRing.Kind] = "Idea" };
        Assert.Equal(1, CryptexEngine.Count(Data, sel, CryptexRing.Kind, "Note"));   // ignores Kind
        Assert.Equal(2, CryptexEngine.Count(Data, sel, CryptexRing.Tag, "ideas"));   // Ideas among kind=Idea
        Assert.Equal(0, CryptexEngine.Count(Data, sel, CryptexRing.Tag, "personal"));// personal is Note only
    }

    [Fact]
    public void DistinctValues_lists_sorted_unique_values_per_ring()
    {
        Assert.Equal(new[] { "Idea", "Note", "Person" }, CryptexEngine.DistinctValues(Data, CryptexRing.Kind));
        Assert.Equal(new[] { "ideas", "personal", "work" }, CryptexEngine.DistinctValues(Data, CryptexRing.Tag));
    }
}
