# Nookryptex Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a cryptex-styled faceted browser page (`/nookryptex`) where five facet wheels (Kind · Tag · Collection · State · People) cross-filter the user's graph, a top band shows the active code / locked node / matching nodes, and an inline add-line creates a node that inherits the dialed-in code.

**Architecture:** A one-shot in-memory dataset (`ICryptexService.GetDatasetAsync`) feeds a pure filtering helper (`CryptexEngine`) so wheel turns need no server round-trips. A Blazor Interactive Server page (`Nookryptex.razor`) holds selection state and renders the top band + add-line, delegating the wheels to `CryptexCylinder.razor`. Adding a node reuses the existing `INodeService`/`ITagService`/`ICollectionService`/`IRelationService`.

**Tech Stack:** .NET 10, Blazor Interactive Server, MudBlazor 9.5, EF Core 10 + SQL Server, xUnit + EF InMemory. Self-contained cryptex CSS in a scoped `<style>` block; one tiny JS helper for the rotate-into-window scroll.

## Global Constraints

- Target framework **net10.0**; follow the existing `IDbContextFactory<NookContext>` short-lived-context service pattern.
- Every read/write is **user-scoped** via `ICurrentUser.GetRequiredUserIdAsync()`; reuse existing services' ownership guards. Never expose another user's nodes/tags/collections/people.
- MudBlazor 9.5 gotchas: `MudTextField` has **no** `AutoGrow`; generic components need explicit `T` (`MudChip<T>` etc.); pass HTML passthrough attributes lowercase.
- Route is exactly **`/nookryptex`**; page title/header text is exactly **`Nookryptex`**.
- New-node defaults when a wheel isn't set: **Kind = Unclassified**, **State = Inbox**.
- Stamping a Person from the code uses the system relation type named **`associated with`**.
- Pure logic is unit-tested with EF InMemory; Razor UI is verified by `dotnet build` + an authenticated runtime smoke (login-then-GET). Do not claim a test passed unless it ran and passed.
- Commit after every task. Do not push.

---

### Task 1: Cryptex models + pure filtering engine

**Files:**
- Create: `Services/CryptexModels.cs`
- Create: `Services/CryptexEngine.cs`
- Test: `Nook.Tests/CryptexEngineTests.cs`

**Interfaces:**
- Produces: `enum CryptexRing { Kind, Tag, Collection, State, People }`; `record CryptexNode(int NodeId, string Title, NodeKind Kind, NodeState State, string? BodyPreview, IReadOnlyList<string> Tags, IReadOnlyList<string> Collections, IReadOnlyList<string> People)`; static `CryptexEngine` with `IEnumerable<string> Values(CryptexNode n, CryptexRing r)`, `bool Matches(CryptexNode n, IReadOnlyDictionary<CryptexRing,string> sel, CryptexRing? ignore = null)`, `List<string> DistinctValues(IEnumerable<CryptexNode> nodes, CryptexRing r)`, `int Count(IEnumerable<CryptexNode> nodes, IReadOnlyDictionary<CryptexRing,string> sel, CryptexRing r, string value)`, `List<CryptexNode> Hits(IEnumerable<CryptexNode> nodes, IReadOnlyDictionary<CryptexRing,string> sel)`.

- [ ] **Step 1: Write the failing tests**

Create `Nook.Tests/CryptexEngineTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test Nook.Tests/Nook.Tests.csproj --filter FullyQualifiedName~CryptexEngineTests`
Expected: FAIL — `CryptexEngine`/`CryptexNode`/`CryptexRing` do not exist (compile error).

- [ ] **Step 3: Create the models**

Create `Services/CryptexModels.cs`:

```csharp
using Nook.Models;

namespace Nook.Services;

/// <summary>The five facet wheels of the Nookryptex.</summary>
public enum CryptexRing { Kind, Tag, Collection, State, People }

/// <summary>
/// A compact, DB-free projection of one node's facet values, used to drive the
/// cryptex entirely in memory. People are the titles of Person-kind nodes this
/// node is related to.
/// </summary>
public sealed record CryptexNode(
    int NodeId,
    string Title,
    NodeKind Kind,
    NodeState State,
    string? BodyPreview,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Collections,
    IReadOnlyList<string> People);
```

- [ ] **Step 4: Create the engine**

Create `Services/CryptexEngine.cs`:

```csharp
namespace Nook.Services;

/// <summary>
/// Pure, DB-free filtering for the cryptex. A selection maps each *set* ring to a
/// single chosen value; a node matches when it carries every selected value.
/// Per-value counts ignore their own ring so a wheel keeps showing its
/// alternatives while narrowing the others.
/// </summary>
public static class CryptexEngine
{
    public static IEnumerable<string> Values(CryptexNode n, CryptexRing r) => r switch
    {
        CryptexRing.Kind => new[] { n.Kind.ToString() },
        CryptexRing.State => new[] { n.State.ToString() },
        CryptexRing.Tag => n.Tags,
        CryptexRing.Collection => n.Collections,
        CryptexRing.People => n.People,
        _ => System.Array.Empty<string>(),
    };

    public static bool Matches(CryptexNode n, IReadOnlyDictionary<CryptexRing, string> sel, CryptexRing? ignore = null)
        => sel.All(kv => kv.Key == ignore || Values(n, kv.Key).Contains(kv.Value));

    public static List<string> DistinctValues(IEnumerable<CryptexNode> nodes, CryptexRing r)
        => nodes.SelectMany(n => Values(n, r)).Distinct().OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToList();

    public static int Count(IEnumerable<CryptexNode> nodes, IReadOnlyDictionary<CryptexRing, string> sel, CryptexRing r, string value)
        => nodes.Count(n => Matches(n, sel, r) && Values(n, r).Contains(value));

    public static List<CryptexNode> Hits(IEnumerable<CryptexNode> nodes, IReadOnlyDictionary<CryptexRing, string> sel)
        => nodes.Where(n => Matches(n, sel)).ToList();
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test Nook.Tests/Nook.Tests.csproj --filter FullyQualifiedName~CryptexEngineTests`
Expected: PASS (5 tests).

- [ ] **Step 6: Commit**

```bash
git add Services/CryptexModels.cs Services/CryptexEngine.cs Nook.Tests/CryptexEngineTests.cs
git commit -m "feat(nookryptex): cryptex models + pure filtering engine"
```

---

### Task 2: CryptexService.GetDatasetAsync + DI registration

**Files:**
- Create: `Services/ICryptexService.cs`
- Create: `Services/CryptexService.cs`
- Modify: `Program.cs` (register the service alongside the other graph services)
- Modify: `Nook.Tests/GraphHarness.cs` (add a `Cryptex(userId)` factory)
- Test: `Nook.Tests/CryptexServiceTests.cs`

**Interfaces:**
- Consumes: `CryptexNode`, `CryptexRing` (Task 1); existing `IDbContextFactory<NookContext>`, `ICurrentUser`, `INodeService`, `ITagService`, `ICollectionService`, `IRelationService`.
- Produces: `interface ICryptexService { Task<List<CryptexNode>> GetDatasetAsync(); Task<int> AddNodeWithCodeAsync(string title, IReadOnlyDictionary<CryptexRing,string> code); }` and `CryptexService`. `AddNodeWithCodeAsync` is implemented in Task 3; in this task it throws `NotImplementedException`.
- Produces (test helper): `GraphHarness.Cryptex(string userId) => CryptexService`.

- [ ] **Step 1: Write the failing tests**

Create `Nook.Tests/CryptexServiceTests.cs`:

```csharp
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
}
```

- [ ] **Step 2: Add the harness factory, then run tests to verify they fail**

Edit `Nook.Tests/GraphHarness.cs` — add this method inside the `GraphHarness` class (next to the existing `Nodes`/`Tags`/`Collections` factories):

```csharp
    public CryptexService Cryptex(string userId) => new(
        Factory, new FakeCurrentUser(userId),
        Nodes(userId), Tags(userId), Collections(userId), Relations(userId));
```

Run: `dotnet test Nook.Tests/Nook.Tests.csproj --filter FullyQualifiedName~CryptexServiceTests`
Expected: FAIL — `ICryptexService`/`CryptexService` do not exist (compile error).

- [ ] **Step 3: Create the interface**

Create `Services/ICryptexService.cs`:

```csharp
namespace Nook.Services;

/// <summary>Data + write operations backing the Nookryptex page.</summary>
public interface ICryptexService
{
    /// <summary>A compact facet projection of all the current user's nodes (incl. archived).</summary>
    Task<List<CryptexNode>> GetDatasetAsync();

    /// <summary>
    /// Creates a node stamped with the dialed-in code: Kind/State (defaulting to
    /// Unclassified/Inbox), an optional tag, collection membership, and an
    /// "associated with" relation to a Person. Returns the new NodeId.
    /// </summary>
    Task<int> AddNodeWithCodeAsync(string title, IReadOnlyDictionary<CryptexRing, string> code);
}
```

- [ ] **Step 4: Create the service (GetDatasetAsync only)**

Create `Services/CryptexService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Nook.Data;
using Nook.Models;

namespace Nook.Services;

public sealed class CryptexService : ICryptexService
{
    private readonly IDbContextFactory<NookContext> _factory;
    private readonly ICurrentUser _currentUser;
    private readonly INodeService _nodes;
    private readonly ITagService _tags;
    private readonly ICollectionService _collections;
    private readonly IRelationService _relations;

    public CryptexService(
        IDbContextFactory<NookContext> factory, ICurrentUser currentUser,
        INodeService nodes, ITagService tags, ICollectionService collections, IRelationService relations)
    {
        _factory = factory;
        _currentUser = currentUser;
        _nodes = nodes;
        _tags = tags;
        _collections = collections;
        _relations = relations;
    }

    public async Task<List<CryptexNode>> GetDatasetAsync()
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();

        var nodes = await db.Nodes.Where(n => n.UserId == userId)
            .Select(n => new { n.NodeId, n.Title, n.Kind, n.State, n.Body })
            .ToListAsync();

        var tags = (await db.NodeTags.Where(nt => nt.Node.UserId == userId)
                .Select(nt => new { nt.NodeId, nt.Tag.Name }).ToListAsync())
            .GroupBy(x => x.NodeId).ToDictionary(g => g.Key, g => g.Select(x => x.Name).ToList());

        var colls = (await db.CollectionMemberships
                .Where(m => m.UserId == userId && m.Collection!.Node!.State != NodeState.Archived)
                .Select(m => new { m.MemberNodeId, Title = m.Collection!.Node!.Title }).ToListAsync())
            .GroupBy(x => x.MemberNodeId).ToDictionary(g => g.Key, g => g.Select(x => x.Title).ToList());

        // People = Person-kind endpoints of any relation the node is part of.
        var rels = await db.NodeRelations.Where(r => r.UserId == userId)
            .Select(r => new {
                r.SourceNodeId, r.TargetNodeId,
                SourceKind = r.SourceNode!.Kind, TargetKind = r.TargetNode!.Kind,
                SourceTitle = r.SourceNode!.Title, TargetTitle = r.TargetNode!.Title })
            .ToListAsync();
        var people = new Dictionary<int, HashSet<string>>();
        void AddPerson(int nodeId, string name) =>
            (people.TryGetValue(nodeId, out var s) ? s : people[nodeId] = new()).Add(name);
        foreach (var r in rels)
        {
            if (r.TargetKind == NodeKind.Person) AddPerson(r.SourceNodeId, r.TargetTitle);
            if (r.SourceKind == NodeKind.Person) AddPerson(r.TargetNodeId, r.SourceTitle);
        }

        return nodes.Select(n => new CryptexNode(
            n.NodeId, n.Title, n.Kind, n.State,
            n.Body is null ? null : (n.Body.Length > 160 ? n.Body[..160] + "…" : n.Body),
            tags.TryGetValue(n.NodeId, out var t) ? t : new List<string>(),
            colls.TryGetValue(n.NodeId, out var c) ? c : new List<string>(),
            people.TryGetValue(n.NodeId, out var p) ? p.ToList() : new List<string>()))
            .ToList();
    }

    public Task<int> AddNodeWithCodeAsync(string title, IReadOnlyDictionary<CryptexRing, string> code)
        => throw new NotImplementedException(); // Task 3
}
```

- [ ] **Step 5: Register the service in DI**

Edit `Program.cs` — add this line in the "Knowledge-graph services" block, right after the `IEventService` registration:

```csharp
builder.Services.AddScoped<ICryptexService, CryptexService>();
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test Nook.Tests/Nook.Tests.csproj --filter FullyQualifiedName~CryptexServiceTests`
Expected: PASS (3 tests).

- [ ] **Step 7: Commit**

```bash
git add Services/ICryptexService.cs Services/CryptexService.cs Program.cs Nook.Tests/GraphHarness.cs Nook.Tests/CryptexServiceTests.cs
git commit -m "feat(nookryptex): cryptex dataset projection service + DI"
```

---

### Task 3: CryptexService.AddNodeWithCodeAsync (stamp the code onto a new node)

**Files:**
- Modify: `Services/CryptexService.cs` (replace the `AddNodeWithCodeAsync` stub)
- Test: `Nook.Tests/CryptexServiceTests.cs` (add cases)

**Interfaces:**
- Consumes: `INodeService.CreateAsync(Node, IEnumerable<int>?)`, `ITagService.GetOrCreateAsync(string,string?)` + `AssignToNodeAsync(int,int)`, `ICollectionService.GetCollectionsAsync(bool)` + `AddMemberAsync(int,int)`, `IRelationService.GetRelationTypesAsync()` + `AddRelationAsync(int,int,int,string?)`.
- Produces: working `AddNodeWithCodeAsync` returning the new `NodeId`.

- [ ] **Step 1: Write the failing tests** — append to `CryptexServiceTests`:

```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Nook.Tests/Nook.Tests.csproj --filter FullyQualifiedName~CryptexServiceTests`
Expected: FAIL — `AddNodeWithCode...` tests throw `NotImplementedException`.

- [ ] **Step 3: Implement AddNodeWithCodeAsync** — replace the stub in `Services/CryptexService.cs`:

```csharp
    public async Task<int> AddNodeWithCodeAsync(string title, IReadOnlyDictionary<CryptexRing, string> code)
    {
        var kind = code.TryGetValue(CryptexRing.Kind, out var k) && Enum.TryParse<NodeKind>(k, out var pk)
            ? pk : NodeKind.Unclassified;
        var state = code.TryGetValue(CryptexRing.State, out var s) && Enum.TryParse<NodeState>(s, out var ps)
            ? ps : NodeState.Inbox;

        var node = await _nodes.CreateAsync(new Node { Title = title.Trim(), Kind = kind, State = state });

        if (code.TryGetValue(CryptexRing.Tag, out var tagName) && !string.IsNullOrWhiteSpace(tagName))
        {
            var tag = await _tags.GetOrCreateAsync(tagName);
            await _tags.AssignToNodeAsync(node.NodeId, tag.TagId);
        }

        if (code.TryGetValue(CryptexRing.Collection, out var collName) && !string.IsNullOrWhiteSpace(collName))
        {
            var collection = (await _collections.GetCollectionsAsync())
                .FirstOrDefault(c => string.Equals(c.Node.Title, collName, StringComparison.OrdinalIgnoreCase));
            if (collection is not null)
                await _collections.AddMemberAsync(collection.Node.NodeId, node.NodeId);
        }

        if (code.TryGetValue(CryptexRing.People, out var personName) && !string.IsNullOrWhiteSpace(personName))
        {
            var userId = await _currentUser.GetRequiredUserIdAsync();
            await using var db = await _factory.CreateDbContextAsync();
            var person = await db.Nodes.FirstOrDefaultAsync(n =>
                n.UserId == userId && n.Kind == NodeKind.Person && n.Title == personName);
            var assoc = (await _relations.GetRelationTypesAsync())
                .FirstOrDefault(r => r.Name == "associated with");
            if (person is not null && assoc is not null)
                await _relations.AddRelationAsync(node.NodeId, person.NodeId, assoc.RelationTypeId);
        }

        return node.NodeId;
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Nook.Tests/Nook.Tests.csproj --filter FullyQualifiedName~CryptexServiceTests`
Expected: PASS (6 tests total in the class).

- [ ] **Step 5: Commit**

```bash
git add Services/CryptexService.cs Nook.Tests/CryptexServiceTests.cs
git commit -m "feat(nookryptex): add-node-with-code stamps kind/state/tag/collection/person"
```

---

### Task 4: CryptexCylinder component (the five wheels)

**Files:**
- Create: `Components/Shared/CryptexCylinder.razor`

**Interfaces:**
- Consumes: `CryptexNode`, `CryptexRing`, `CryptexEngine` (Task 1); `NodeUi.Icon` for kind glyphs.
- Produces: component with parameters `List<CryptexNode> Dataset`, `Dictionary<CryptexRing,string> Selection`, `EventCallback<(CryptexRing Ring, string Value)> OnSelect`. Renders five vertical wheels; clicking a value invokes `OnSelect`. Emits items with class `item sel` on the selected value (the page's JS scrolls those to center).

- [ ] **Step 1: Create the component**

Create `Components/Shared/CryptexCylinder.razor`:

```razor
@* The five-wheel cryptex. Pure presentation: it reads the dataset + selection
   and raises OnSelect((ring,value)); the parent owns the state. *@

<div class="nkx-cylinder">
    <div class="nkx-window"></div>
    <div class="nkx-rings">
        @foreach (var ring in Rings)
        {
            <div class="nkx-col">
                <div class="nkx-rlabel">@ring.ToString()</div>
                <div class="nkx-rail">
                    <div class="nkx-pad"></div>
                    <div class="nkx-items">
                        @foreach (var v in CryptexEngine.DistinctValues(Dataset, ring))
                        {
                            var count = CryptexEngine.Count(Dataset, Selection, ring, v);
                            var selected = Selection.TryGetValue(ring, out var sv) && sv == v;
                            var cls = selected ? "nkx-item sel" : (count == 0 ? "nkx-item dim" : "nkx-item");
                            <div class="@cls" @onclick="@(() => Pick(ring, v, count, selected))">
                                @Prefix(ring, v)@v@if (count > 0) { <span class="nkx-n">@count</span>}
                            </div>
                        }
                    </div>
                    <div class="nkx-pad"></div>
                </div>
            </div>
        }
    </div>
</div>

@code {
    [Parameter, EditorRequired] public List<CryptexNode> Dataset { get; set; } = new();
    [Parameter, EditorRequired] public Dictionary<CryptexRing, string> Selection { get; set; } = new();
    [Parameter] public EventCallback<(CryptexRing Ring, string Value)> OnSelect { get; set; }

    private static readonly CryptexRing[] Rings =
        { CryptexRing.Kind, CryptexRing.Tag, CryptexRing.Collection, CryptexRing.State, CryptexRing.People };

    private Task Pick(CryptexRing ring, string value, int count, bool selected)
        => (count == 0 && !selected) ? Task.CompletedTask : OnSelect.InvokeAsync((ring, value));

    private static string Prefix(CryptexRing r, string v) => r switch
    {
        CryptexRing.Kind => IconGlyph(v),
        CryptexRing.Tag => "#",
        CryptexRing.People => "🧑 ",
        _ => "",
    };

    private static string IconGlyph(string kind) => kind switch
    {
        "Note" => "📝 ", "Idea" => "💡 ", "Event" => "📅 ", "Person" => "🧑 ",
        "Project" => "📁 ", "Bookmark" => "🔖 ", "Place" => "📍 ", "Collection" => "🗂 ",
        _ => "⚡ ",
    };
}
```

- [ ] **Step 2: Build to verify the component compiles**

Run: `dotnet build Nook.csproj -clp:ErrorsOnly`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add Components/Shared/CryptexCylinder.razor
git commit -m "feat(nookryptex): cryptex cylinder component (five wheels)"
```

---

### Task 5: Nookryptex page, styling, JS scroll, and nav link

**Files:**
- Create: `Components/Pages/Nookryptex.razor`
- Create: `wwwroot/js/nookryptex.js`
- Modify: `Components/App.razor` (reference the JS file)
- Modify: `Components/Layout/NavMenu.razor` (add the Nookryptex link)

**Interfaces:**
- Consumes: `ICryptexService`, `CryptexEngine`, `CryptexCylinder`, `CryptexNode`, `CryptexRing`, `IJSRuntime`, `NavigationManager`, `ISnackbar`.
- Produces: routable page `/nookryptex`.

- [ ] **Step 1: Create the JS scroll helper**

Create `wwwroot/js/nookryptex.js`:

```javascript
// Rotate the selected value of each wheel into the center window (progressive
// enhancement; the page works without it).
window.nkxScrollCenter = function () {
  document.querySelectorAll('.nkx .nkx-item.sel').forEach(function (el) {
    el.scrollIntoView({ block: 'center', behavior: 'smooth' });
  });
};
```

- [ ] **Step 2: Reference the script from the host page**

Edit `Components/App.razor` — add this line immediately **before** the closing `</body>` (next to the existing Blazor script reference):

```razor
    <script src="js/nookryptex.js"></script>
```

- [ ] **Step 3: Add the nav link**

Edit `Components/Layout/NavMenu.razor` — add this `MudNavLink` right after the `"/all"` link:

```razor
    <MudNavLink Href="/nookryptex" Icon="@Icons.Material.Filled.Lock">Nookryptex</MudNavLink>
```

- [ ] **Step 4: Create the page**

Create `Components/Pages/Nookryptex.razor`:

```razor
@page "/nookryptex"
@inject ICryptexService Cryptex
@inject NavigationManager Nav
@inject IJSRuntime JS

<PageTitle>Nookryptex · Nook</PageTitle>

<div class="nkx">
    <p class="nkx-title">Nookryptex</p>
    <p class="nkx-hint">Turn the wheels; the code and matches update above. Add a node and it inherits the code.</p>

    <div class="nkx-nav">
        <a href="/today">Today</a><a href="/inbox">Inbox</a><a href="/all">All</a><a href="/people">People</a>
        <a href="/collections">Collections</a><a href="/actions">Actions</a><a href="/events">Events</a><a href="/timeline">Timeline</a>
    </div>

    @if (_loading)
    {
        <MudProgressLinear Indeterminate="true" Color="Color.Primary" />
    }
    else
    {
        var hits = CryptexEngine.Hits(_all, _sel);
        var node = _focusId is int fid ? _all.FirstOrDefault(n => n.NodeId == fid)
                   : (hits.Count == 1 ? hits[0] : null);

        <div class="nkx-top">
            @* The code *@
            <div class="nkx-sec code">
                <div class="nkx-label">The code</div>
                <div class="nkx-content">
                    @if (_sel.Count == 0)
                    {
                        <div class="nkx-empty">No wheels set. Every node is in view.</div>
                    }
                    else
                    {
                        @foreach (var kv in _sel)
                        {
                            <div class="nkx-chip" @onclick="@(() => Clear(kv.Key))"><span>@kv.Value</span><span class="x">✕</span></div>
                        }
                    }
                    <div class="nkx-tally"><b>@hits.Count</b> nodes align</div>
                </div>
                @if (_sel.Count > 0)
                {
                    <div class="nkx-foot"><button class="nkx-reset" @onclick="Reset">↺ Reset</button></div>
                }
            </div>

            @* Locked / focused node *@
            <div class="nkx-sec detail">
                <div class="nkx-label">@(node is null ? "Locked" : (node.NodeId == _freshId ? "Just added" : (hits.Count == 1 && _focusId is null ? "Locked" : "Focused")))</div>
                <div class="nkx-content">
                    @if (node is null)
                    {
                        <div class="nkx-placeholder">Turn the wheels to narrow to one node, or pick a match →</div>
                    }
                    else
                    {
                        <div class="nkx-nodecard">
                            <div class="nkx-kind">@KindGlyph(node.Kind)</div>
                            <div>
                                <h2>@node.Title</h2>
                                <div class="nkx-meta">@node.Kind · @node.State@(node.Tags.Count > 0 ? " · " + string.Join(" ", node.Tags.Select(t => "#" + t)) : "")</div>
                                @if (!string.IsNullOrWhiteSpace(node.BodyPreview))
                                {
                                    <div class="nkx-body">@node.BodyPreview</div>
                                }
                                <div class="nkx-stats"><span><b>@node.Collections.Count</b> collections</span><span><b>@node.People.Count</b> people</span><span><b>@node.Tags.Count</b> tags</span></div>
                            </div>
                        </div>
                    }
                </div>
                @if (node is not null)
                {
                    <div class="nkx-foot"><button class="nkx-open" @onclick="@(() => Open(node.NodeId))">Open node →</button></div>
                }
            </div>

            @* Matching nodes *@
            <div class="nkx-sec matches">
                <div class="nkx-label">Matching nodes · @hits.Count</div>
                <div class="nkx-content">
                    @if (hits.Count == 0)
                    {
                        <div class="nkx-empty">No matches.</div>
                    }
                    else
                    {
                        @foreach (var n in hits)
                        {
                            var rcls = "nkx-row" + (_focusId == n.NodeId ? " active" : "") + (n.NodeId == _freshId ? " fresh" : "");
                            <div class="@rcls" @onclick="@(() => Focus(n.NodeId))">
                                <span class="ri">@KindGlyph(n.Kind)</span><span class="rt">@n.Title</span><span class="rk">@n.Kind</span>
                            </div>
                        }
                    }
                </div>
            </div>
        </div>

        @* New item line — inherits the code *@
        <div class="nkx-add">
            <span class="plus">＋</span>
            <input class="nkx-addinput" placeholder="Add a new node…" @bind="_newTitle" @bind:event="oninput"
                   @onkeydown="@(async e => { if (e.Key == "Enter") await Add(); })" />
            <span class="nkx-willbe">
                @if (_sel.Count == 0)
                {
                    <span>plain node · Unclassified · Inbox</span>
                }
                else
                {
                    <span>inherits</span>
                    @foreach (var kv in _sel)
                    {
                        <span class="tag">@kv.Value</span>
                    }
                }
            </span>
            <button class="nkx-addbtn" disabled="@(string.IsNullOrWhiteSpace(_newTitle) || _saving)" @onclick="Add">Add</button>
        </div>

        <CryptexCylinder Dataset="_all" Selection="_sel" OnSelect="OnSelect" />
    }
</div>

<style>
    /* self-contained cryptex theme, scoped under .nkx */
    .nkx{--brass:#c8a45c;--brass-dim:#8a7440;--parch:#e8e0cf;--muted:#8b93a3;--ok:#5bbf8a;--toph:206px;--h:372px;
        color:var(--parch);font-family:'Segoe UI',system-ui,sans-serif}
    .nkx *{box-sizing:border-box}
    .nkx-title{font-size:24px;letter-spacing:.16em;text-transform:uppercase;color:var(--brass);font-weight:700;text-align:center;margin:0 0 3px}
    .nkx-hint{text-align:center;color:var(--muted);font-size:13px;margin:0 0 14px}
    .nkx-nav{display:flex;flex-wrap:wrap;gap:8px;justify-content:center;margin-bottom:16px}
    .nkx-nav a{font-size:12.5px;color:var(--parch);text-decoration:none;padding:6px 12px;border:1px solid #333c50;border-radius:999px;background:linear-gradient(#232b3c,#1a2030)}
    .nkx-nav a:hover{border-color:var(--brass-dim);color:#fff}
    .nkx-top{display:flex;gap:14px;margin-bottom:14px}
    .nkx-sec{height:var(--toph);display:flex;flex-direction:column;overflow:hidden;border:1px solid #2c3446;border-radius:14px;background:linear-gradient(#1d2433,#161c28);padding:12px 14px}
    .nkx-sec.code{flex:1 1 180px;min-width:160px}
    .nkx-sec.detail{flex:2 1 300px;min-width:260px;border-top:3px solid var(--brass)}
    .nkx-sec.matches{flex:1.5 1 240px;min-width:220px}
    .nkx-label{flex:none;font-size:11px;letter-spacing:.2em;text-transform:uppercase;color:var(--brass-dim);margin-bottom:8px}
    .nkx-content{flex:1;overflow-y:auto}
    .nkx-foot{flex:none;margin-top:8px}
    .nkx-chip{display:flex;align-items:center;justify-content:space-between;gap:6px;background:#0e1420;border:1px solid var(--brass-dim);color:var(--brass);padding:5px 10px;border-radius:8px;font-size:13px;cursor:pointer;margin-bottom:6px}
    .nkx-chip .x{color:var(--muted)}.nkx-chip:hover .x{color:#fff}
    .nkx-empty{color:var(--muted);font-size:12.5px}
    .nkx-tally{margin-top:8px;font-size:12.5px;color:var(--muted)}.nkx-tally b{color:var(--ok);font-size:19px;display:block}
    .nkx-reset,.nkx-open{cursor:pointer;border-radius:8px;font-size:12.5px}
    .nkx-reset{color:var(--muted);background:none;border:1px dashed #3a4358;border-radius:999px;padding:5px 12px;width:100%}
    .nkx-reset:hover{color:var(--parch);border-color:var(--brass-dim)}
    .nkx-open{border:1px solid #d8b968;background:linear-gradient(#e2c274,#b6923f);color:#20180a;font-weight:600;padding:7px 12px}
    .nkx-nodecard{display:flex;gap:12px;align-items:flex-start}.nkx-kind{font-size:26px}
    .nkx-nodecard h2{margin:0 0 4px;font-size:19px}.nkx-meta{color:var(--muted);font-size:12.5px}
    .nkx-body{color:#cfd6e4;font-size:13.5px;margin-top:6px}
    .nkx-stats{display:flex;flex-wrap:wrap;gap:12px;margin-top:10px;font-size:12.5px;color:var(--muted)}.nkx-stats b{color:var(--parch)}
    .nkx-placeholder{color:var(--muted);font-size:13px;display:flex;height:100%;align-items:center;justify-content:center;text-align:center}
    .nkx-row{display:flex;align-items:center;gap:9px;padding:7px 9px;border-radius:9px;border:1px solid #262e40;background:#191f2d;cursor:pointer;margin-bottom:6px}
    .nkx-row:hover{border-color:var(--brass-dim)}.nkx-row.active{border-color:#e2c274;background:#20293a}.nkx-row.fresh{border-color:#5bbf8a55}
    .nkx-row .ri{font-size:16px}.nkx-row .rt{flex:1;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font-size:13px}.nkx-row .rk{font-size:10.5px;color:var(--muted)}
    .nkx-add{display:flex;align-items:center;gap:12px;border:1px solid var(--brass-dim);border-radius:12px;background:linear-gradient(#20283a,#181e2c);padding:9px 12px;margin-bottom:16px}
    .nkx-add .plus{width:30px;height:30px;flex:none;display:flex;align-items:center;justify-content:center;border-radius:8px;background:linear-gradient(#e2c274,#b6923f);color:#20180a;font-size:20px;font-weight:700}
    .nkx-addinput{flex:1;min-width:80px;background:transparent;border:none;outline:none;color:var(--parch);font-size:15px}
    .nkx-addinput::placeholder{color:var(--muted)}
    .nkx-willbe{display:flex;align-items:center;gap:6px;flex-wrap:wrap;color:var(--muted);font-size:12px}
    .nkx-willbe .tag{background:#0e1420;border:1px solid var(--brass-dim);color:var(--brass);padding:2px 8px;border-radius:6px;font-size:12px}
    .nkx-addbtn{cursor:pointer;border:1px solid #d8b968;background:linear-gradient(#e2c274,#b6923f);color:#20180a;font-weight:700;padding:8px 16px;border-radius:8px;font-size:13px;flex:none}
    .nkx-addbtn:disabled{opacity:.4;cursor:default}
    .nkx-cylinder{position:relative;display:flex;height:var(--h);border:1px solid #33404f;border-radius:16px;overflow:hidden;background:linear-gradient(90deg,#0d1017,#171d2a 12%,#1b2230 50%,#171d2a 88%,#0d1017);box-shadow:0 16px 44px #0009}
    .nkx-cylinder::before,.nkx-cylinder::after{content:"";position:absolute;top:0;bottom:0;width:14px;z-index:3;pointer-events:none}
    .nkx-cylinder::before{left:0;background:linear-gradient(90deg,#b6923f,#8a7440 40%,transparent)}
    .nkx-cylinder::after{right:0;background:linear-gradient(-90deg,#b6923f,#8a7440 40%,transparent)}
    .nkx-window{position:absolute;left:14px;right:14px;top:50%;height:46px;transform:translateY(-50%);z-index:2;pointer-events:none;border-top:1px solid #e2c27455;border-bottom:1px solid #e2c27455;background:linear-gradient(#e2c2740f,#e2c27400,#e2c2740f)}
    .nkx-rings{display:flex;flex:1;min-width:0}
    .nkx-col{flex:1;min-width:0;position:relative;border-right:1px solid #222a38;display:flex;flex-direction:column}.nkx-col:last-child{border-right:none}
    .nkx-rlabel{z-index:4;text-align:center;font-size:11px;letter-spacing:.16em;text-transform:uppercase;color:var(--brass-dim);padding:9px 2px 7px;background:linear-gradient(#171d2a,#171d2ad0)}
    .nkx-rail{flex:1;overflow-y:auto;scroll-behavior:smooth;-webkit-mask-image:linear-gradient(#0000,#000 20%,#000 80%,#0000);mask-image:linear-gradient(#0000,#000 20%,#000 80%,#0000)}
    .nkx-pad{height:150px}
    .nkx-items{display:flex;flex-direction:column;align-items:center;gap:6px;padding:0 8px}
    .nkx-item{width:100%;text-align:center;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;cursor:pointer;user-select:none;padding:8px;border-radius:8px;font-size:14px;color:var(--parch);border:1px solid transparent}
    .nkx-item:hover{background:#222a3a;border-color:#2f3850}
    .nkx-item.sel{color:#241b09;font-weight:700;border-color:#e2c274;background:linear-gradient(#f0d38f,#c9a352);box-shadow:0 0 0 2px #e2c27455}
    .nkx-item.dim{opacity:.24;filter:grayscale(.5)}
    .nkx-n{color:var(--muted);font-size:12px;margin-left:5px}.nkx-item.sel .nkx-n{color:#4d3d17}
    @@media (max-width:820px){.nkx-top{flex-wrap:wrap}.nkx-cylinder{height:320px}}
</style>

@code {
    private List<CryptexNode> _all = new();
    private readonly Dictionary<CryptexRing, string> _sel = new();
    private int? _focusId;
    private int? _freshId;
    private string? _newTitle;
    private bool _loading = true;
    private bool _saving;

    protected override async Task OnInitializedAsync()
    {
        _all = await Cryptex.GetDatasetAsync();
        _loading = false;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        try { await JS.InvokeVoidAsync("nkxScrollCenter"); } catch { /* JS optional */ }
    }

    private Task OnSelect((CryptexRing Ring, string Value) pick)
    {
        if (_sel.TryGetValue(pick.Ring, out var cur) && cur == pick.Value) _sel.Remove(pick.Ring);
        else _sel[pick.Ring] = pick.Value;
        _focusId = null;
        return Task.CompletedTask;
    }

    private void Clear(CryptexRing ring) { _sel.Remove(ring); _focusId = null; }
    private void Reset() { _sel.Clear(); _focusId = null; _freshId = null; }
    private void Focus(int id) => _focusId = id;
    private void Open(int id) => Nav.NavigateTo($"/nodes/{id}");

    private async Task Add()
    {
        if (string.IsNullOrWhiteSpace(_newTitle) || _saving) return;
        _saving = true;
        try
        {
            var id = await Cryptex.AddNodeWithCodeAsync(_newTitle!, _sel);
            _all = await Cryptex.GetDatasetAsync();
            _freshId = id;
            _focusId = id;
            _newTitle = null;
        }
        finally { _saving = false; }
    }

    private static string KindGlyph(NodeKind kind) => kind switch
    {
        NodeKind.Note => "📝", NodeKind.Idea => "💡", NodeKind.Event => "📅", NodeKind.Person => "🧑",
        NodeKind.Project => "📁", NodeKind.Bookmark => "🔖", NodeKind.Place => "📍", NodeKind.Collection => "🗂",
        _ => "⚡",
    };
}
```

- [ ] **Step 5: Build the solution**

Run: `dotnet build Nook.sln -clp:ErrorsOnly`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 6: Runtime smoke — authenticated render of /nookryptex**

Start the app against a scratch LocalDB and confirm the page renders for the demo user with no server exceptions:

```bash
cd /c/Users/capnb/source/repos/Nook
export ASPNETCORE_ENVIRONMENT=Development
export ConnectionStrings__DefaultConnection='Server=(localdb)\MSSQLLocalDB;Database=NookNkx;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True'
sqlcmd -S '(localdb)\MSSQLLocalDB' -Q "IF DB_ID('NookNkx') IS NOT NULL BEGIN ALTER DATABASE [NookNkx] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [NookNkx]; END"
dotnet run --project Nook.csproj --urls http://localhost:5223 > /tmp/nkx.log 2>&1 &
```

Then (PowerShell) log in and GET the page, asserting content + no exceptions:

```powershell
$login = Invoke-WebRequest "http://localhost:5223/login" -SessionVariable s -UseBasicParsing
$tok = ([regex]'name="__RequestVerificationToken"[^>]*value="([^"]*)"').Match($login.Content).Groups[1].Value
Invoke-WebRequest "http://localhost:5223/login" -Method Post -WebSession $s -UseBasicParsing -Body @{ 'Input.Email'='demo@nook.local'; 'Input.Password'='Demo123!'; '__RequestVerificationToken'=$tok; '_handler'='login' } | Out-Null
$r = Invoke-WebRequest "http://localhost:5223/nookryptex" -WebSession $s -UseBasicParsing
"HTTP $($r.StatusCode); has title: $($r.Content -match 'Nookryptex'); has wheels: $($r.Content -match 'nkx-cylinder')"
```

Expected: `HTTP 200; has title: True; has wheels: True`. Then confirm no real exceptions:
`grep -ciE "Unhandled exception|NullReferenceException|InvalidOperationException" /tmp/nkx.log` → expect `0`.

Stop the app (`Get-NetTCPConnection -LocalPort 5223 -State Listen | %{ Stop-Process -Id $_.OwningProcess -Force }`) and drop `NookNkx`.

- [ ] **Step 7: Commit**

```bash
git add Components/Pages/Nookryptex.razor wwwroot/js/nookryptex.js Components/App.razor Components/Layout/NavMenu.razor
git commit -m "feat(nookryptex): page, cryptex theme, add-line, nav link, JS scroll"
```

---

## Self-Review

**1. Spec coverage:**
- Route `/nookryptex`, title "Nookryptex" → Task 5. ✓
- Five wheels Kind·Tag·Collection·State·People with cross-filtering + counts + dim → Task 1 (engine) + Task 4 (render). ✓
- Top band: code (removable chips + tally + reset), locked/focused detail (+ Open node → `/nodes/{id}`), matching nodes (click to focus) → Task 5. ✓
- New-item line inheriting the code (Kind/State/Tag/Collection/Person), defaults Unclassified/Inbox → Task 3 + Task 5. ✓
- Nav buttons → real routes → Task 5. ✓
- One-shot dataset incl. archived; People = related Person nodes → Task 2. ✓
- User scoping / cross-user safety → Tasks 2 & 3 (tests). ✓
- Minimal JS scroll, progressive enhancement → Task 5. ✓

**2. Placeholder scan:** No TBD/TODO; every code step shows complete code; every test step shows the test and the run command with expected output. ✓

**3. Type consistency:** `CryptexNode`, `CryptexRing`, and the `CryptexEngine` signatures used in Tasks 4–5 match Task 1. `ICryptexService.GetDatasetAsync()`/`AddNodeWithCodeAsync(string, IReadOnlyDictionary<CryptexRing,string>)` are defined in Task 2 and consumed identically in Tasks 3 & 5. `GraphHarness.Cryptex(userId)` (Task 2) is used in Tasks 2 & 3 tests. CSS class names (`nkx-*`, `nkx-item sel`) match between the component (Task 4) and the JS selector + styles (Task 5). ✓
