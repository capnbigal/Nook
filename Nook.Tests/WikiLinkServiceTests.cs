using System.Linq;
using Nook.Models;
using Nook.Services;
using Xunit;

namespace Nook.Tests;

public class WikiLinkServiceTests
{
    private static WikiLinkService Svc(GraphHarness h, string user) =>
        new(h.Nodes(user), h.Relations(user));

    [Fact]
    public async Task ResolveOrCreate_finds_existing_owned_node_by_exact_title()
    {
        var h = new GraphHarness();
        var existing = await h.Nodes("u").CreateAsync(new Node { Title = "Coffee", State = NodeState.Active });

        var (id, url) = await Svc(h, "u").ResolveOrCreateAsync("Coffee");

        Assert.Equal(existing.NodeId, id);
        Assert.Equal($"/nodes/{existing.NodeId}", url);
    }

    [Fact]
    public async Task ResolveOrCreate_creates_unclassified_inbox_node_when_missing()
    {
        var h = new GraphHarness();
        var (id, url) = await Svc(h, "u").ResolveOrCreateAsync("Brand New");

        var created = await h.Nodes("u").GetByIdAsync(id);
        Assert.NotNull(created);
        Assert.Equal("Brand New", created!.Title);
        Assert.Equal(NodeKind.Unclassified, created.Kind);
        Assert.Equal(NodeState.Inbox, created.State);
        Assert.Equal($"/nodes/{id}", url);
    }

    [Fact]
    public async Task ResolveOrCreate_finds_exact_title_even_when_search_window_is_full_of_substring_matches()
    {
        var h = new GraphHarness();
        var nodes = h.Nodes("u");

        // The exact-title node is created first (older) and never pinned, so under the
        // old Contains-search-then-filter approach it would fall outside a Take(25) window
        // once 30+ more-recently-updated, pinned nodes merely CONTAINING "Meeting" exist.
        var exact = await nodes.CreateAsync(new Node { Title = "Meeting", State = NodeState.Active });

        for (var i = 0; i < 30; i++)
        {
            await nodes.CreateAsync(new Node
            {
                Title = $"Standup notes {i}",
                Body = "Recap of yesterday's Meeting agenda.",
                State = NodeState.Active,
                IsPinned = true,
            });
        }

        var countBefore = (await nodes.QueryAsync(new NodeFilter { Take = 1000 })).Count;

        var (id, url) = await Svc(h, "u").ResolveOrCreateAsync("Meeting");

        Assert.Equal(exact.NodeId, id);
        Assert.Equal($"/nodes/{exact.NodeId}", url);

        var countAfter = (await nodes.QueryAsync(new NodeFilter { Take = 1000 })).Count;
        Assert.Equal(countBefore, countAfter); // no duplicate Inbox node created
    }

    [Fact]
    public async Task ResolveOrCreate_returns_sentinel_for_blank_title_and_creates_nothing()
    {
        var h = new GraphHarness();
        var nodes = h.Nodes("u");
        var countBefore = (await nodes.QueryAsync(new NodeFilter { Take = 1000 })).Count;

        var (id, url) = await Svc(h, "u").ResolveOrCreateAsync("   ");

        Assert.Equal(0, id);
        Assert.Equal(string.Empty, url);

        var countAfter = (await nodes.QueryAsync(new NodeFilter { Take = 1000 })).Count;
        Assert.Equal(countBefore, countAfter);
    }

    [Fact]
    public async Task ResolveOrCreate_ignores_cross_user_titles()
    {
        var h = new GraphHarness();
        var othersNode = await h.Nodes("a").CreateAsync(new Node { Title = "Shared", State = NodeState.Active });

        var (id, _) = await Svc(h, "b").ResolveOrCreateAsync("Shared");

        Assert.NotEqual(othersNode.NodeId, id); // b gets its own new node, not a's
    }

    [Fact]
    public async Task Reconcile_adds_missing_and_removes_stale_mentions()
    {
        var h = new GraphHarness();
        await h.Migration().SeedSystemDataAsync(); // seeds the "mentions" relation type
        var src = await h.Nodes("u").CreateAsync(new Node { Title = "Source", State = NodeState.Active });
        var svc = Svc(h, "u");

        await svc.ReconcileAsync(src.NodeId, new[] { "Alpha", "Beta" });
        var afterFirst = await h.Relations("u").GetConnectionsAsync(src.NodeId);
        Assert.Equal(2, afterFirst.Outgoing.Count(c => c.Label == "mentions"));

        // Drop Beta, add Gamma.
        await svc.ReconcileAsync(src.NodeId, new[] { "Alpha", "Gamma" });
        var afterSecond = await h.Relations("u").GetConnectionsAsync(src.NodeId);
        var mentioned = afterSecond.Outgoing.Where(c => c.Label == "mentions").Select(c => c.OtherTitle).ToList();
        Assert.Equal(2, mentioned.Count);
        Assert.Contains("Alpha", mentioned);
        Assert.Contains("Gamma", mentioned);
        Assert.DoesNotContain("Beta", mentioned); // stale removed
    }

    [Fact]
    public async Task Reconcile_does_not_touch_non_mentions_relations()
    {
        var h = new GraphHarness();
        await h.Migration().SeedSystemDataAsync();
        var relSvc = h.Relations("u");
        var nodeSvc = h.Nodes("u");
        var src = await nodeSvc.CreateAsync(new Node { Title = "Source", State = NodeState.Active });
        var other = await nodeSvc.CreateAsync(new Node { Title = "Other", State = NodeState.Active });
        var relatedTo = (await relSvc.GetRelationTypesAsync()).First(r => r.Name == "related to");
        await relSvc.AddRelationAsync(src.NodeId, other.NodeId, relatedTo.RelationTypeId);

        var svc = Svc(h, "u");
        await svc.ReconcileAsync(src.NodeId, new[] { "Alpha" });

        var conns = await relSvc.GetConnectionsAsync(src.NodeId);
        Assert.Contains(conns.Outgoing, c => c.Label == "related to" && c.OtherNodeId == other.NodeId);
        Assert.Contains(conns.Outgoing, c => c.Label == "mentions" && c.OtherTitle == "Alpha");
        Assert.Equal(2, conns.Outgoing.Count);

        // Reconcile again with an empty link set: "mentions" to Alpha should be removed,
        // but the unrelated "related to" relation must remain untouched.
        await svc.ReconcileAsync(src.NodeId, Array.Empty<string>());
        var afterEmpty = await relSvc.GetConnectionsAsync(src.NodeId);
        Assert.DoesNotContain(afterEmpty.Outgoing, c => c.Label == "mentions");
        Assert.Contains(afterEmpty.Outgoing, c => c.Label == "related to" && c.OtherNodeId == other.NodeId);
    }
}
