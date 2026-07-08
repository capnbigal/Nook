# Nook Spine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the "spine" of the Nook redesign — an adaptive workspace shell, a ⌘K command palette, and a document-first Node page with a client-side rich editor — on the unchanged Node graph.

**Architecture:** A new CSS-grid `WorkspaceShell` replaces the MudBlazor drawer layout; navigation emerges from the user's own graph (Favorites/Pinned/Collections/Recents via existing services). The Node page becomes a document whose body is Markdown in `Node.Body`, edited by a browser-owned TipTap module (JS interop) that only round-trips on a debounced save. All backend change is additive: one new table (`UserPreference`), a handful of additive service methods, and per-kind color helpers — no edits to the Node/relation schema or existing service contracts.

**Tech Stack:** .NET 10 Blazor Interactive Server, MudBlazor 9.5 (themed), EF Core / SQL Server, TipTap (vendored ESM bundle via `<ImportMap>`), xUnit (+ EF InMemory GraphHarness).

## Global Constraints

- Stay Blazor Interactive Server — no WASM, no render-mode changes to the app shell.
- NO app-level npm/build pipeline except the isolated `/editor-src` bundling (its hashed output is vendored and committed; the app build never needs Node).
- All backend changes additive — never edit `INodeService`/`IRelationService` existing methods or the `Node`/`NodeRelation` schema.
- User/node FKs use `DeleteBehavior.Restrict`.
- Enum members are string-stored.
- New scoped services are constructed via `IDbContextFactory<NookContext>` + `ICurrentUser`.
- Branch off `main`, commit frequently.
- Services + pure-logic get real xUnit via `GraphHarness`; `.razor`/`.razor.js` get `dotnet build` + manual verification — no bUnit.

---

### Task 1: Prove the dynamic JS-module interop + disposal pattern with a throwaway spike

*Stream: EDITOR-INTEROP · orderHint 10 · Depends on: none*

**Files:**
- Create `Components/Nodes/InteropSpike.razor` (throwaway `@page "/interop-spike"`, InteractiveServer, IAsyncDisposable)
- Create `Components/Nodes/InteropSpike.razor.js` (collocated ES module; dynamically imported)
- Test: none — there is NO Blazor component test harness (no bUnit). Verification is manual `dotnet build` + `dotnet watch` observation, per the testing-reality rules. Do NOT fabricate a unit test.

Note: `Components/Nodes/` does not exist yet — this task creates the folder. The existing `Components/Layout/ReconnectModal.razor` uses a STATIC `<script type="module" src=@Assets[...]>` tag, which is the OPPOSITE of the dynamic-import pattern the editor needs — that is exactly why this spike exists.

**Interfaces:**
- Consumes: `IJSRuntime` (via `JS.InvokeAsync<IJSObjectReference>("import", "./Components/Nodes/InteropSpike.razor.js")`), `DotNetObjectReference<T>`, `Microsoft.JSInterop.JSDisconnectedException`. Nothing from other streams.
- Produces: no exported .NET/JS interface. Its deliverable is a PROVEN, copy-ready pattern (path resolution of a collocated `.razor.js`, JS→.NET `[JSInvokable]` roundtrip via `DotNetObjectReference`, and clean `IAsyncDisposable` teardown swallowing `JSDisconnectedException`) that Task 3 (EditorHost) reuses verbatim. De-risks Blazor Server interop BEFORE the editor depends on it.
- Disposition: retained as the hidden `/interop-spike` smoke route until Task 3 lands; Task 3's final step deletes both spike files once EditorHost proves the same pattern in real use.

**Steps:**

- [ ] **Step 1: Create the spike component with the dynamic-import lifecycle.** Write `Components/Nodes/InteropSpike.razor`:

```razor
@page "/interop-spike"
@rendermode InteractiveServer
@implements IAsyncDisposable
@using Microsoft.JSInterop
@inject IJSRuntime JS

<h1>Interop Spike</h1>
<p>Module state: <strong>@_state</strong></p>
<p>Last JS&rarr;.NET message: <code>@_lastMessage</code></p>

@code {
    private IJSObjectReference? _module;
    private DotNetObjectReference<InteropSpike>? _ref;
    private string _state = "loading";
    private string _lastMessage = "(none)";

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender || _module is not null) return; // guard: import ONCE
        _ref = DotNetObjectReference.Create(this);
        _module = await JS.InvokeAsync<IJSObjectReference>(
            "import", "./Components/Nodes/InteropSpike.razor.js");
        await _module.InvokeVoidAsync("initialize", _ref);
        _state = "initialized";
        StateHasChanged();
    }

    [JSInvokable]
    public Task PingFromJs(string message)
    {
        _lastMessage = message;
        return InvokeAsync(StateHasChanged);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_module is not null)
            {
                await _module.InvokeVoidAsync("dispose");
                await _module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException) { /* circuit already gone — expected on nav-away */ }
        _ref?.Dispose();
    }
}
```

- [ ] **Step 2: Create the collocated JS module.** Write `Components/Nodes/InteropSpike.razor.js`:

```js
let dotNetRef = null;
let timer = null;

export function initialize(ref) {
    dotNetRef = ref;
    console.log("[InteropSpike] module imported + path resolved OK");
    // immediate JS -> .NET roundtrip
    dotNetRef.invokeMethodAsync("PingFromJs", "hello from JS @ " + new Date().toISOString());
    // recurring roundtrip proves the circuit stays live
    timer = setInterval(() => {
        dotNetRef?.invokeMethodAsync("PingFromJs", "tick " + Date.now());
    }, 2000);
}

export function dispose() {
    console.log("[InteropSpike] dispose() called — clearing timer");
    if (timer) clearInterval(timer);
    timer = null;
    dotNetRef = null;
}
```

Path note: the import specifier `./Components/Nodes/InteropSpike.razor.js` resolves against `<base href="/">`, and Blazor serves collocated `.razor.js` files at `/Components/Nodes/InteropSpike.razor.js` as static web assets — no `@Assets[...]` needed for the dynamic-import string.

- [ ] **Step 3: Build.** Run:

```
dotnet build Nook.sln
```

Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 4: Manual verification (no test harness exists — this is an honest manual check).** Run `dotnet watch` and open `http://localhost:5176/interop-spike`. Confirm ALL of:
  1. Page shows `Module state: initialized` (dynamic import resolved).
  2. Browser devtools console logs `[InteropSpike] module imported + path resolved OK`.
  3. `Last JS→.NET message` updates immediately to the `hello from JS` string, then changes every 2s (`tick …`) — proves the `[JSInvokable]` roundtrip.
  4. Navigate away (click to any other route). Console logs `dispose() called`; NO unhandled `JSDisconnectedException` appears in the terminal or console. Refresh + re-navigate a few times to confirm no leaked timers/errors.

- [ ] **Step 5: Commit.** Branch off main first (do NOT commit to main):

```
git checkout -b feature/editor-interop
git add Components/Nodes/InteropSpike.razor Components/Nodes/InteropSpike.razor.js
git commit -m "spike: prove dynamic .razor.js import + DotNetObjectReference roundtrip + disposal"
```

(End the commit message with the required `Co-Authored-By` trailer.) Leave `/interop-spike` in place as a smoke route; Task 3 deletes it.

---

### Task 2: Add NodeFilter.Take and honor it in NodeService.QueryAsync

*Stream: backend-additive · orderHint 20 · Depends on: none*

> **Contract:** produces `NodeFilter.Take : int?` (null = unbounded) — consumed by Task 6 (WikiLinkService), Task 19 (CommandPalette), Task 23 (EditorHost). `INodeService.QueryAsync` signature is unchanged.

**Files:**
- Modify: `Services/NodeFilter.cs` — add `public int? Take { get; set; }`
- Modify: `Services/NodeService.cs:63-66` — apply `Take` after `OrderBy`, before `ToListAsync`
- Test: `Nook.Tests/NodeQueryTakeTests.cs` (Create)

**Interfaces:**
- Produces: `NodeFilter.Take : int?` (null = unbounded)
- Consumes: existing `Task<List<Node>> INodeService.QueryAsync(NodeFilter filter)` — unchanged signature; ordering stays `OrderByDescending(IsPinned).ThenByDescending(UpdatedAt)` with `Take` applied last so it caps the ordered result.

**Steps:**

- [ ] **Step 1: Write the failing test.** Create `Nook.Tests/NodeQueryTakeTests.cs`. Copy the harness style from `NodeServiceTests.cs` exactly (`new GraphHarness()`, `h.Nodes("u")`).
```csharp
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
```
- [ ] **Step 2: Run it red.**
```
dotnet test Nook.sln --filter "FullyQualifiedName~NodeQueryTakeTests"
```
Expect a **compile error** (`NodeFilter` has no `Take`) — that is the red state.
- [ ] **Step 3: Add the property.** In `Services/NodeFilter.cs`, after the `UnassignedOnly` property, add:
```csharp
    /// <summary>Cap the number of results (applied after ordering). null = unbounded.</summary>
    public int? Take { get; set; }
```
- [ ] **Step 4: Apply it in QueryAsync.** In `Services/NodeService.cs`, replace the final `return await query...ToListAsync();` (lines ~63-66) with:
```csharp
        query = query
            .OrderByDescending(n => n.IsPinned)
            .ThenByDescending(n => n.UpdatedAt);

        if (filter.Take is int t) query = query.Take(t);

        return await query.ToListAsync();
```
- [ ] **Step 5: Run it green.**
```
dotnet test Nook.sln --filter "FullyQualifiedName~NodeQueryTakeTests"
```
Expect **Passed! - Failed: 0**. Then `dotnet build Nook.sln` → 0 errors.
- [ ] **Step 6: Commit.** `git add -A && git commit` with message `feat(nodes): NodeFilter.Take caps QueryAsync results after ordering`.

---

### Task 3: Add INodeService.SaveBodyAsync (body-only, user-scoped)

*Stream: backend-additive · orderHint 21 · Depends on: none*

> **Contract:** produces `Task INodeService.SaveBodyAsync(int nodeId, string? body, CancellationToken ct = default)` — consumed by EditorHost's `SaveBodyAsync` JSInvokable (Task 23). Signatures match.

**Files:**
- Modify: `Services/INodeService.cs` — add method under the Mutations region
- Modify: `Services/NodeService.cs` — implement (own short-lived context; sets Body + UpdatedAt ONLY)
- Test: `Nook.Tests/NodeSaveBodyTests.cs` (Create)

**Interfaces:**
- Produces: `Task INodeService.SaveBodyAsync(int nodeId, string? body, CancellationToken ct = default)` — loads the *owned* node (`UserId == currentUser`), sets `Body` and `UpdatedAt` only, `SaveChanges`. MUST NOT touch tags, relations, kind, state, title, url. No-op if node not owned/found. Distinct method — does NOT reuse `UpdateAsync`.
- Consumed by (later UI band): `EditorHost.SaveBodyAsync` JSInvokable.

**Steps:**

- [ ] **Step 1: Write the failing test.** Create `Nook.Tests/NodeSaveBodyTests.cs`:
```csharp
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
```
- [ ] **Step 2: Run it red.**
```
dotnet test Nook.sln --filter "FullyQualifiedName~NodeSaveBodyTests"
```
Expect compile error (`SaveBodyAsync` not defined).
- [ ] **Step 3: Declare it on the interface.** In `Services/INodeService.cs`, under the `// ---- Mutations ----` region add:
```csharp
    /// <summary>Persist only the Body (and UpdatedAt) of an owned node. Never touches tags/relations/kind/state.</summary>
    Task SaveBodyAsync(int nodeId, string? body, CancellationToken ct = default);
```
- [ ] **Step 4: Implement it.** In `Services/NodeService.cs`, add (do NOT route through `UpdateAsync` or `MutateAsync`, so the intent stays body-only and it can carry the `ct`):
```csharp
    public async Task SaveBodyAsync(int nodeId, string? body, CancellationToken ct = default)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync(ct);
        var node = await db.Nodes.FirstOrDefaultAsync(n => n.NodeId == nodeId && n.UserId == userId, ct);
        if (node is null) return;
        node.Body = string.IsNullOrWhiteSpace(body) ? null : body;
        node.UpdatedAt = DateTime.UtcNow; // ApplyTimestamps also bumps this on Modified
        await db.SaveChangesAsync(ct);
    }
```
(Ensure `using Microsoft.EntityFrameworkCore;` is already present — it is at the top of NodeService.cs.)
- [ ] **Step 5: Run it green.**
```
dotnet test Nook.sln --filter "FullyQualifiedName~NodeSaveBodyTests"
```
Expect **Failed: 0**. Then `dotnet build Nook.sln` → 0 errors.
- [ ] **Step 6: Commit.** `git commit` with `feat(nodes): SaveBodyAsync persists body-only, user-scoped`.

---

### Task 4: Add NodeUi.KindAccent and KindAccentVar kind-color map

*Stream: backend-additive · orderHint 22 · Depends on: none*

> **Contract:** canonical producer of `NodeUi.KindAccent`/`KindAccentVar` and `Nook.Tests/NodeUiTests.cs`. Task 9 (VISUAL) is an identical duplicate; hex values here MUST equal the `--kind-*` tokens in `nook-tokens.css` (Task 7). Consumed by Tasks 11, 16, 24, 26.

**Files:**
- Modify: `Services/NodeUi.cs` — add two static pure methods (keep existing Icon/StateColor/etc.)
- Test: `Nook.Tests/NodeUiTests.cs` (Create)

**Interfaces:**
- Produces: `static string NodeUi.KindAccent(NodeKind kind)` → 7-char `#RRGGBB` hex from the 16-kind spec map. `static string NodeUi.KindAccentVar(NodeKind kind)` → CSS var name `"--kind-<lowercasekind>"` (e.g. `--kind-note`).
- Consumed by (UI band): `ObjectTypeBadge.razor`, `NodeHeader`, sidebar links, `nook-tokens.css` var names must match `KindAccentVar` output.

**Steps:**

- [ ] **Step 1: Write the failing test (pure — no DB).** Create `Nook.Tests/NodeUiTests.cs`:
```csharp
using System;
using Nook.Models;
using Nook.Services;
using Xunit;

namespace Nook.Tests;

public class NodeUiTests
{
    [Theory]
    [InlineData(NodeKind.Note, "#4C7DF0", "--kind-note")]
    [InlineData(NodeKind.Project, "#7C5CFF", "--kind-project")]
    [InlineData(NodeKind.Unclassified, "#8C8578", "--kind-unclassified")]
    [InlineData(NodeKind.Event, "#EE5D9A", "--kind-event")]
    public void KindAccent_matches_spec(NodeKind kind, string hex, string var)
    {
        Assert.Equal(hex, NodeUi.KindAccent(kind));
        Assert.Equal(var, NodeUi.KindAccentVar(kind));
    }

    [Fact]
    public void Every_kind_has_a_valid_hex_and_var()
    {
        foreach (NodeKind kind in Enum.GetValues<NodeKind>())
        {
            var hex = NodeUi.KindAccent(kind);
            Assert.Matches("^#[0-9A-Fa-f]{6}$", hex);
            Assert.StartsWith("--kind-", NodeUi.KindAccentVar(kind));
        }
    }
}
```
- [ ] **Step 2: Run it red.**
```
dotnet test Nook.sln --filter "FullyQualifiedName~NodeUiTests"
```
Expect compile error (`KindAccent` undefined).
- [ ] **Step 3: Implement both methods.** In `Services/NodeUi.cs`, add inside the class (the full 16-kind spec map):
```csharp
    /// <summary>Per-kind accent color (hex) from the design spec's 16-kind map.</summary>
    public static string KindAccent(NodeKind kind) => kind switch
    {
        NodeKind.Unclassified => "#8C8578",
        NodeKind.Note         => "#4C7DF0",
        NodeKind.Journal      => "#E0863C",
        NodeKind.Observation  => "#14B8A6",
        NodeKind.Idea         => "#F5B417",
        NodeKind.Reference    => "#5878A6",
        NodeKind.Bookmark     => "#F2416A",
        NodeKind.List         => "#23A968",
        NodeKind.Person       => "#F76F5A",
        NodeKind.Project      => "#7C5CFF",
        NodeKind.Place        => "#A15C34",
        NodeKind.Organization => "#A24C8C",
        NodeKind.Topic        => "#17B0C4",
        NodeKind.Resource     => "#7E9C24",
        NodeKind.Collection   => "#B78430",
        NodeKind.Event        => "#EE5D9A",
        _ => "#8C8578",
    };

    /// <summary>The CSS custom-property name backing a kind's accent (e.g. --kind-note).</summary>
    public static string KindAccentVar(NodeKind kind) =>
        "--kind-" + kind.ToString().ToLowerInvariant();
```
- [ ] **Step 4: Run it green.**
```
dotnet test Nook.sln --filter "FullyQualifiedName~NodeUiTests"
```
Expect **Failed: 0**. Then `dotnet build Nook.sln` → 0 errors.
- [ ] **Step 5: Commit.** `git commit` with `feat(ui): NodeUi.KindAccent/KindAccentVar 16-kind color map`.

---

### Task 5: Add UserPreference model, EF config, AddUserPreference migration, and UserPreferenceService

*Stream: backend-additive · orderHint 23 · Depends on: none*

> **Contract:** produces `IUserPreferenceService` (GetOrCreate/SetDarkMode/SetSidebarCollapsed/PushRecent/SetLastOpened/GetRecentIds) — consumed by WorkspaceState/ThemeState (Task 13). Signatures match Task 13's Consumes exactly.

**Files:**
- Create: `Models/UserPreference.cs`
- Create: `Services/IUserPreferenceService.cs`
- Create: `Services/UserPreferenceService.cs`
- Modify: `Data/NookContext.cs` — `DbSet<UserPreference>` + OnModelCreating config (folded into ConfigureGraph)
- Modify: `Program.cs:70-76` — register `AddScoped<IUserPreferenceService, UserPreferenceService>()`
- Modify: `Nook.Tests/GraphHarness.cs` — add `Prefs(userId)` accessor
- Create (via EF): `Data/Migrations/*_AddUserPreference.cs` (+ Designer + snapshot update)
- Test: `Nook.Tests/UserPreferenceServiceTests.cs` (Create)

**Interfaces:**
- Produces model `Nook.Models.UserPreference { int Id; string UserId; bool IsDarkMode; bool SidebarCollapsed; int? LastOpenedNodeId; string RecentNodeIdsCsv = ""; DateTime UpdatedAt; }`.
- Produces `IUserPreferenceService`: `Task<UserPreference> GetOrCreateAsync(CancellationToken ct=default)`, `Task SetDarkModeAsync(bool on)`, `Task SetSidebarCollapsedAsync(bool collapsed)`, `Task PushRecentAsync(int nodeId)` (MRU, dedupe, cap 12, CSV in `RecentNodeIdsCsv`), `Task SetLastOpenedAsync(int nodeId)`, `Task<IReadOnlyList<int>> GetRecentIdsAsync(CancellationToken ct=default)`. All user-scoped via `ICurrentUser`.
- Constructor: `UserPreferenceService(IDbContextFactory<NookContext> factory, ICurrentUser currentUser)`.
- Consumed by (UI band): `WorkspaceState`, `ThemeState`.

**Steps:**

- [ ] **Step 1: Create the model.** `Models/UserPreference.cs`:
```csharp
using System.ComponentModel.DataAnnotations;

namespace Nook.Models;

/// <summary>Per-user workspace preferences (one row per user).</summary>
public class UserPreference
{
    public int Id { get; set; }

    /// <summary>Owner. FK to AspNetUsers; unique (one row per user).</summary>
    public string UserId { get; set; } = string.Empty;

    public bool IsDarkMode { get; set; }
    public bool SidebarCollapsed { get; set; }

    /// <summary>Denormalised last-opened node id (no FK — must survive node deletion).</summary>
    public int? LastOpenedNodeId { get; set; }

    /// <summary>MRU recent node ids, most-recent-first, comma-separated. Capped at 12.</summary>
    [MaxLength(200)]
    public string RecentNodeIdsCsv { get; set; } = "";

    public DateTime UpdatedAt { get; set; }
}
```
- [ ] **Step 2: Register the DbSet + config.** In `Data/NookContext.cs` add the DbSet beside the graph sets (after line 41):
```csharp
    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();
```
Then at the end of `ConfigureGraph(...)` (before the closing brace of the method) add:
```csharp
        modelBuilder.Entity<UserPreference>(entity =>
        {
            entity.Property(e => e.UserId).IsRequired();
            entity.Property(e => e.RecentNodeIdsCsv).HasMaxLength(200);
            entity.HasIndex(e => e.UserId).IsUnique();
            // FK to the identity user, Restrict per house style.
            entity.HasOne<ApplicationUser>().WithMany()
                  .HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Restrict);
            // LastOpenedNodeId is denormalised — deliberately NO FK.
        });
```
- [ ] **Step 3: Write the failing test.** Create `Nook.Tests/UserPreferenceServiceTests.cs`. Construct the service directly from the harness `Factory` + `FakeCurrentUser` (same wiring the harness uses):
```csharp
using System.Linq;
using Nook.Services;
using Xunit;

namespace Nook.Tests;

public class UserPreferenceServiceTests
{
    private static UserPreferenceService Svc(GraphHarness h, string user) =>
        new(h.Factory, new FakeCurrentUser(user));

    [Fact]
    public async Task GetOrCreate_is_idempotent_per_user()
    {
        var h = new GraphHarness();
        var a = await Svc(h, "u").GetOrCreateAsync();
        var b = await Svc(h, "u").GetOrCreateAsync();
        Assert.Equal(a.Id, b.Id); // same row, not a duplicate
    }

    [Fact]
    public async Task PushRecent_dedupes_caps_12_and_MRU_orders()
    {
        var h = new GraphHarness();
        var svc = Svc(h, "u");
        for (var i = 1; i <= 14; i++) await svc.PushRecentAsync(i);
        await svc.PushRecentAsync(5); // re-visit -> jumps to front, no dupe

        var recent = await svc.GetRecentIdsAsync();
        Assert.Equal(12, recent.Count);
        Assert.Equal(5, recent[0]);
        Assert.Equal(recent.Distinct().Count(), recent.Count);
        Assert.DoesNotContain(1, recent); // oldest evicted past cap
    }

    [Fact]
    public async Task SetDarkMode_persists()
    {
        var h = new GraphHarness();
        await Svc(h, "u").SetDarkModeAsync(true);
        Assert.True((await Svc(h, "u").GetOrCreateAsync()).IsDarkMode);
    }

    [Fact]
    public async Task Preferences_are_user_isolated()
    {
        var h = new GraphHarness();
        await Svc(h, "a").SetDarkModeAsync(true);
        Assert.False((await Svc(h, "b").GetOrCreateAsync()).IsDarkMode);
    }
}
```
- [ ] **Step 4: Run it red.**
```
dotnet test Nook.sln --filter "FullyQualifiedName~UserPreferenceServiceTests"
```
Expect compile error (`IUserPreferenceService`/`UserPreferenceService` undefined).
- [ ] **Step 5: Create the interface.** `Services/IUserPreferenceService.cs`:
```csharp
using Nook.Models;

namespace Nook.Services;

public interface IUserPreferenceService
{
    Task<UserPreference> GetOrCreateAsync(CancellationToken ct = default);
    Task SetDarkModeAsync(bool on);
    Task SetSidebarCollapsedAsync(bool collapsed);
    Task PushRecentAsync(int nodeId);
    Task SetLastOpenedAsync(int nodeId);
    Task<IReadOnlyList<int>> GetRecentIdsAsync(CancellationToken ct = default);
}
```
- [ ] **Step 6: Implement the service.** `Services/UserPreferenceService.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Nook.Data;
using Nook.Models;

namespace Nook.Services;

public sealed class UserPreferenceService : IUserPreferenceService
{
    private const int RecentCap = 12;
    private readonly IDbContextFactory<NookContext> _factory;
    private readonly ICurrentUser _currentUser;

    public UserPreferenceService(IDbContextFactory<NookContext> factory, ICurrentUser currentUser)
    {
        _factory = factory;
        _currentUser = currentUser;
    }

    private static async Task<UserPreference> LoadOrCreateAsync(NookContext db, string userId, CancellationToken ct)
    {
        var pref = await db.UserPreferences.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (pref is null)
        {
            pref = new UserPreference { UserId = userId, RecentNodeIdsCsv = "", UpdatedAt = DateTime.UtcNow };
            db.UserPreferences.Add(pref);
            await db.SaveChangesAsync(ct);
        }
        return pref;
    }

    public async Task<UserPreference> GetOrCreateAsync(CancellationToken ct = default)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await LoadOrCreateAsync(db, userId, ct);
    }

    public Task SetDarkModeAsync(bool on) => MutateAsync(p => p.IsDarkMode = on);
    public Task SetSidebarCollapsedAsync(bool collapsed) => MutateAsync(p => p.SidebarCollapsed = collapsed);
    public Task SetLastOpenedAsync(int nodeId) => MutateAsync(p => p.LastOpenedNodeId = nodeId);

    public Task PushRecentAsync(int nodeId) => MutateAsync(p =>
    {
        var ids = Parse(p.RecentNodeIdsCsv);
        ids.RemoveAll(x => x == nodeId);
        ids.Insert(0, nodeId);
        if (ids.Count > RecentCap) ids = ids.GetRange(0, RecentCap);
        p.RecentNodeIdsCsv = string.Join(',', ids);
    });

    public async Task<IReadOnlyList<int>> GetRecentIdsAsync(CancellationToken ct = default)
    {
        var pref = await GetOrCreateAsync(ct);
        return Parse(pref.RecentNodeIdsCsv);
    }

    private async Task MutateAsync(Action<UserPreference> mutate)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        var pref = await LoadOrCreateAsync(db, userId, default);
        mutate(pref);
        pref.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    private static List<int> Parse(string csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? new List<int>()
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries)
                 .Select(s => int.TryParse(s, out var v) ? v : 0)
                 .Where(v => v > 0).ToList();
}
```
- [ ] **Step 7: Register in DI + add harness accessor.** In `Program.cs`, after the knowledge-graph `AddScoped` block (~line 76) add:
```csharp
builder.Services.AddScoped<IUserPreferenceService, UserPreferenceService>();
```
In `Nook.Tests/GraphHarness.cs`, add beside the other accessors:
```csharp
    public UserPreferenceService Prefs(string userId) => new(Factory, new FakeCurrentUser(userId));
```
(Optional: the test above constructs the service directly; the accessor keeps the harness complete for later tasks.)
- [ ] **Step 8: Run it green.**
```
dotnet test Nook.sln --filter "FullyQualifiedName~UserPreferenceServiceTests"
```
Expect **Failed: 0** (InMemory builds the schema from the model — no migration needed for tests).
- [ ] **Step 9: Create the real migration + build.** From the repo root (project holding `NookContext`):
```
dotnet ef migrations add AddUserPreference -o Data/Migrations
dotnet build Nook.sln
```
Expect a new `*_AddUserPreference.cs` creating the `UserPreferences` table (unique index on `UserId`, FK `UserId`→`AspNetUsers` ON DELETE NO ACTION/Restrict, `LastOpenedNodeId` nullable with no FK) plus an updated `NookContextModelSnapshot.cs`, and a clean build. If `dotnet ef` is missing: `dotnet tool install --global dotnet-ef` first.
- [ ] **Step 10: Commit.** `git add -A && git commit` with `feat(prefs): UserPreference model, service, DI, and AddUserPreference migration`.

---

### Task 6: Add IWikiLinkService/WikiLinkService for [[wiki-link]] resolve and reconcile

*Stream: backend-additive · orderHint 24 · Depends on: Task 2 (NodeFilter.Take)*

> **Contract:** consumes `NodeFilter.Take` (Task 2); produces `ResolveOrCreateAsync`/`ReconcileAsync` consumed by EditorHost (Task 23). `url` format is `"/nodes/{id}"`, matching NodePage's route (Task 27).

**Files:**
- Create: `Services/IWikiLinkService.cs`
- Create: `Services/WikiLinkService.cs`
- Modify: `Program.cs` — register `AddScoped<IWikiLinkService, WikiLinkService>()`
- Modify: `Nook.Tests/GraphHarness.cs` — add `WikiLinks(userId)` accessor
- Test: `Nook.Tests/WikiLinkServiceTests.cs` (Create)

**Interfaces:**
- Produces `IWikiLinkService`: `Task<(int nodeId, string url)> ResolveOrCreateAsync(string title, CancellationToken ct=default)` and `Task ReconcileAsync(int sourceNodeId, IReadOnlyCollection<string> linkedTitles, CancellationToken ct=default)`.
- Consumes existing (unchanged) signatures: `INodeService.QueryAsync(NodeFilter{ SearchText, Take })`, `INodeService.QuickCaptureAsync(string title, string? body=null)` (→ Unclassified/Inbox), `IRelationService.GetRelationTypesAsync()` (find the seeded `"mentions"` type — Name=="mentions"), `IRelationService.GetConnectionsAsync(sourceNodeId).Outgoing` (each `Connection` has `NodeRelationId`, `OtherNodeId`, `Label`==RelationType.Name for outgoing), `IRelationService.AddRelationAsync(source, target, relationTypeId)`, `IRelationService.RemoveRelationAsync(nodeRelationId)`.
- Constructor: `WikiLinkService(INodeService nodes, IRelationService relations)` — user scoping flows through those services.
- Returned `url` format: `"/nodes/{id}"`.
- Consumed by (UI band): `EditorHost` JSInvokables `ResolveOrCreateLinkAsync` and `SaveBodyAsync`→reconcile.

**Steps:**

- [ ] **Step 1: Write the failing test.** Create `Nook.Tests/WikiLinkServiceTests.cs`. Seed the system `"mentions"` relation type with `h.Migration().SeedSystemDataAsync()` (same as `NodeServiceTests.Delete_...`). Build the service from the harness' per-user `INodeService`/`IRelationService`:
```csharp
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
}
```
- [ ] **Step 2: Run it red.**
```
dotnet test Nook.sln --filter "FullyQualifiedName~WikiLinkServiceTests"
```
Expect compile error (`WikiLinkService` undefined).
- [ ] **Step 3: Create the interface.** `Services/IWikiLinkService.cs`:
```csharp
namespace Nook.Services;

/// <summary>Resolves [[wiki-link]] titles to owned nodes and reconciles "mentions" relations.</summary>
public interface IWikiLinkService
{
    /// <summary>Find an owned node by exact Title; if none, quick-capture an Unclassified inbox node. Returns id + "/nodes/{id}".</summary>
    Task<(int nodeId, string url)> ResolveOrCreateAsync(string title, CancellationToken ct = default);

    /// <summary>Diff the given [[titles]] against existing outgoing "mentions" relations from the source; add missing, remove stale.</summary>
    Task ReconcileAsync(int sourceNodeId, IReadOnlyCollection<string> linkedTitles, CancellationToken ct = default);
}
```
- [ ] **Step 4: Implement the service.** `Services/WikiLinkService.cs` — note `GetConnectionsAsync(...).Outgoing` where `Connection.Label == "mentions"` and `Connection.OtherNodeId` is the target; stale removal uses `Connection.NodeRelationId`:
```csharp
using Nook.Models;

namespace Nook.Services;

public sealed class WikiLinkService : IWikiLinkService
{
    private const string MentionsRelationName = "mentions";
    private readonly INodeService _nodes;
    private readonly IRelationService _relations;

    public WikiLinkService(INodeService nodes, IRelationService relations)
    {
        _nodes = nodes;
        _relations = relations;
    }

    public async Task<(int nodeId, string url)> ResolveOrCreateAsync(string title, CancellationToken ct = default)
    {
        var trimmed = (title ?? string.Empty).Trim();
        // Contains-match then exact-title filter, scoped to the current user by NodeService.
        var candidates = await _nodes.QueryAsync(new NodeFilter { SearchText = trimmed, Take = 25 });
        var match = candidates.FirstOrDefault(n =>
            string.Equals(n.Title, trimmed, StringComparison.OrdinalIgnoreCase));
        var id = match?.NodeId ?? (await _nodes.QuickCaptureAsync(trimmed)).NodeId;
        return (id, $"/nodes/{id}");
    }

    public async Task ReconcileAsync(int sourceNodeId, IReadOnlyCollection<string> linkedTitles, CancellationToken ct = default)
    {
        // Resolve desired targets (dedupe by resolved node id; drop self-links).
        var desired = new HashSet<int>();
        foreach (var t in linkedTitles.Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            var (id, _) = await ResolveOrCreateAsync(t, ct);
            if (id != sourceNodeId) desired.Add(id);
        }

        var mentionsType = (await _relations.GetRelationTypesAsync())
            .FirstOrDefault(rt => rt.Name == MentionsRelationName);
        if (mentionsType is null) return; // seed data missing; nothing to reconcile against

        var connections = await _relations.GetConnectionsAsync(sourceNodeId);
        var existing = connections.Outgoing
            .Where(c => c.Label == MentionsRelationName)
            .ToList();
        var existingTargets = existing.Select(c => c.OtherNodeId).ToHashSet();

        // Add missing.
        foreach (var targetId in desired.Where(id => !existingTargets.Contains(id)))
            await _relations.AddRelationAsync(sourceNodeId, targetId, mentionsType.RelationTypeId);

        // Remove stale.
        foreach (var stale in existing.Where(c => !desired.Contains(c.OtherNodeId)))
            await _relations.RemoveRelationAsync(stale.NodeRelationId);
    }
}
```
- [ ] **Step 5: Register in DI + add harness accessor.** In `Program.cs`, after the `IUserPreferenceService` registration (or the graph block) add:
```csharp
builder.Services.AddScoped<IWikiLinkService, WikiLinkService>();
```
In `Nook.Tests/GraphHarness.cs`, add:
```csharp
    public WikiLinkService WikiLinks(string userId) => new(Nodes(userId), Relations(userId));
```
- [ ] **Step 6: Run it green.**
```
dotnet test Nook.sln --filter "FullyQualifiedName~WikiLinkServiceTests"
```
Expect **Failed: 0**. Then `dotnet build Nook.sln` → 0 errors.
- [ ] **Step 7: Commit.** `git add -A && git commit` with `feat(wikilink): WikiLinkService resolve-or-create + mentions reconcile`.

---

### Task 7: Add nook-tokens.css design-token layer and wire it into App.razor after MudBlazor

*Stream: VISUAL-SYSTEM · orderHint 30 · Depends on: none*

> **Contract:** the `--kind-*` hexes here MUST equal `NodeUi.KindAccent` (Task 4). This file's `@font-face` placeholder is filled by Task 8; palette-overlay styles are appended by Task 19; `[data-theme="dark"]` is what the theme switch (Tasks 12/13/14) resolves to.

**Files:**
- Create: `wwwroot/css/nook-tokens.css` (full custom-property token layer)
- Modify: `Components/App.razor:14` (add token stylesheet link AFTER the MudBlazor.min.css link, so our vars win the cascade)
- Test: none — CSS + markup only. Verify via `dotnet build` + a throwaway swatch / DevTools computed-vars inspection (no component test harness exists; do NOT fabricate one).

**Interfaces:**
- Produces: CSS custom properties on `:root` and `[data-theme="dark"]` consumed by every later component. Exact names (other streams hard-code these):
  - Kind accents: `--kind-unclassified` … `--kind-event` (16, one per `NodeKind`), matching `Services/NodeUi.KindAccentVar`.
  - Brand: `--brand-iris:#5B54E8; --brand-tangerine:#F98A3C;`
  - Surfaces (light default): `--canvas:#F7F5F0; --card:#FFFFFF; --ink:#262119; --line:#E9E4D9;`
  - Surfaces (dark, under `[data-theme="dark"]`): `--canvas:#16130E; --surface:#201C15; --ink:#F3EEE4;`
  - Scale tokens: `--space-1..--space-8`, `--radius-sm/md/lg`, `--motion-fast/base/slow`, `--ease-standard`.
- Consumes: nothing (base layer). `Services/ThemeState` (band 40+) flips `<html data-theme>`; this file defines what that switch resolves to.

**Steps:**

- [ ] **Step 1: Branch off main.** This is the first task of the VISUAL-SYSTEM stream, so create the working branch before touching files.
```bash
cd /c/Users/capnb/source/repos/Nook
git checkout main && git pull --ff-only
git checkout -b feat/visual-system
```
Expect: `Switched to a new branch 'feat/visual-system'`.

- [ ] **Step 2: Write `wwwroot/css/nook-tokens.css`.** Define the light defaults on `:root`, dark overrides on `[data-theme="dark"]`, the full 16-kind accent map, and scale/motion tokens. Use the EXACT hexes from the contract.
```css
/* nook-tokens.css — design-token layer. Loaded AFTER MudBlazor so it wins. */
:root {
  /* Brand */
  --brand-iris: #5B54E8;
  --brand-tangerine: #F98A3C;

  /* Surfaces — Sand light (default) */
  --canvas: #F7F5F0;
  --card: #FFFFFF;
  --surface: #FFFFFF;
  --ink: #262119;
  --line: #E9E4D9;

  /* Kind accents (order matches Models/GraphEnums.cs NodeKind) */
  --kind-unclassified: #8C8578;
  --kind-note: #4C7DF0;
  --kind-journal: #E0863C;
  --kind-observation: #14B8A6;
  --kind-idea: #F5B417;
  --kind-reference: #5878A6;
  --kind-bookmark: #F2416A;
  --kind-list: #23A968;
  --kind-person: #F76F5A;
  --kind-project: #7C5CFF;
  --kind-place: #A15C34;
  --kind-organization: #A24C8C;
  --kind-topic: #17B0C4;
  --kind-resource: #7E9C24;
  --kind-collection: #B78430;
  --kind-event: #EE5D9A;

  /* Spacing (4px base) */
  --space-1: 4px;  --space-2: 8px;  --space-3: 12px; --space-4: 16px;
  --space-5: 24px; --space-6: 32px; --space-7: 48px; --space-8: 64px;

  /* Radius */
  --radius-sm: 6px; --radius-md: 10px; --radius-lg: 16px;

  /* Motion */
  --motion-fast: 120ms; --motion-base: 200ms; --motion-slow: 320ms;
  --ease-standard: cubic-bezier(0.2, 0, 0, 1);
}

[data-theme="dark"] {
  --canvas: #16130E;
  --card: #201C15;
  --surface: #201C15;
  --ink: #F3EEE4;
  --line: #2C271E;
}

@media (prefers-reduced-motion: reduce) {
  :root { --motion-fast: 0ms; --motion-base: 0ms; --motion-slow: 0ms; }
}
```
(The `@font-face` rules are added by the NEXT task — leave a comment placeholder at the top of the file so that task has an anchor: `/* @font-face rules injected by fonts task */`.)

- [ ] **Step 3: Wire into `Components/App.razor` AFTER MudBlazor css.** The MudBlazor link is line 14. Add ours immediately after so `--*` tokens and any resets override MudBlazor defaults.
```razor
    <link rel="stylesheet" href="_content/MudBlazor/MudBlazor.min.css" />
    <link rel="stylesheet" href="@Assets["css/nook-tokens.css"]" />
```

- [ ] **Step 4: Build-verify.**
```bash
dotnet build Nook.sln
```
Expect: `Build succeeded. 0 Error(s)`.

- [ ] **Step 5: Manual token verify.** `dotnet watch` (project root), open http://localhost:5176, open DevTools console and confirm the vars resolve:
```js
getComputedStyle(document.documentElement).getPropertyValue('--kind-note')  // " #4C7DF0"
getComputedStyle(document.documentElement).getPropertyValue('--canvas')     // " #F7F5F0"
document.documentElement.dataset.theme='dark';
getComputedStyle(document.documentElement).getPropertyValue('--canvas')     // " #16130E"
```
Observe: light returns the Sand hexes; setting `data-theme='dark'` flips `--canvas`/`--ink` to the dark hexes. Reset with `delete document.documentElement.dataset.theme`.

- [ ] **Step 6: Commit.**
```bash
git add wwwroot/css/nook-tokens.css Components/App.razor
git commit -m "feat(visual): add nook-tokens.css design-token layer"
```

---

### Task 8: Self-host Bricolage/Figtree/JetBrains Mono fonts and drop the Google Fonts Roboto link

*Stream: VISUAL-SYSTEM · orderHint 31 · Depends on: Task 7 (nook-tokens.css)*

> **Contract:** produces `--font-display`/`--font-body`/`--font-mono` and the three exact `font-family` names (`Bricolage Grotesque`, `Figtree`, `JetBrains Mono`) that `NookTheme` (Task 10) passes to MudBlazor `Typography`. Edits Task 7's `@font-face` placeholder.

**Files:**
- Create: `wwwroot/fonts/BricolageGrotesque-Variable.woff2`, `wwwroot/fonts/Figtree-Variable.woff2`, `wwwroot/fonts/JetBrainsMono-Variable.woff2` (committed binaries — download the variable woff2 from the font sources)
- Modify: `wwwroot/css/nook-tokens.css` (add `@font-face` block + set the display/body/mono family CSS vars)
- Modify: `Components/App.razor:12-13` (REMOVE the `preconnect` to fonts.googleapis.com and the Roboto `<link>`)
- Test: none — assets + CSS. Verify via build + browser Network tab (no fonts.googleapis.com request).

**Interfaces:**
- Produces CSS vars consumed by `NookTheme.cs` and all components: `--font-display: 'Bricolage Grotesque', system-ui, sans-serif;`, `--font-body: 'Figtree', system-ui, sans-serif;`, `--font-mono: 'JetBrains Mono', ui-monospace, monospace;`. The three `font-family` NAMES ('Bricolage Grotesque', 'Figtree', 'JetBrains Mono') are the exact strings `NookTheme` passes to MudBlazor `Typography`.
- Consumes: `wwwroot/css/nook-tokens.css` from the previous task (edits its font placeholder comment).

**Steps:**

- [ ] **Step 1: Obtain the variable woff2 files and commit them.** These are binary assets, not code — download the variable-axis woff2 for each family and save under `wwwroot/fonts/` with the exact names below (rename on download). Sources: Bricolage Grotesque + Figtree from Google Fonts (SIL OFL), JetBrains Mono from JetBrains (SIL OFL). Target names:
```
wwwroot/fonts/BricolageGrotesque-Variable.woff2
wwwroot/fonts/Figtree-Variable.woff2
wwwroot/fonts/JetBrainsMono-Variable.woff2
```
Verify presence:
```bash
ls -la /c/Users/capnb/source/repos/Nook/wwwroot/fonts/*.woff2
```
Expect: three non-zero-byte files.

- [ ] **Step 2: Add `@font-face` + font vars to `nook-tokens.css`.** Replace the placeholder comment from the previous task with the face declarations (variable weight range) and add the three font vars to `:root`.
```css
@font-face {
  font-family: 'Bricolage Grotesque';
  src: url('../fonts/BricolageGrotesque-Variable.woff2') format('woff2');
  font-weight: 200 800; font-display: swap; font-style: normal;
}
@font-face {
  font-family: 'Figtree';
  src: url('../fonts/Figtree-Variable.woff2') format('woff2');
  font-weight: 300 900; font-display: swap; font-style: normal;
}
@font-face {
  font-family: 'JetBrains Mono';
  src: url('../fonts/JetBrainsMono-Variable.woff2') format('woff2');
  font-weight: 100 800; font-display: swap; font-style: normal;
}
```
And inside `:root` add:
```css
  --font-display: 'Bricolage Grotesque', system-ui, sans-serif;
  --font-body: 'Figtree', system-ui, sans-serif;
  --font-mono: 'JetBrains Mono', ui-monospace, monospace;
```
(Paths are relative to the CSS file at `wwwroot/css/`, so `../fonts/…`.)

- [ ] **Step 3: Remove the Google Fonts links from `Components/App.razor`.** Delete lines 12-13 (the `preconnect` and the Roboto stylesheet). Do NOT remove the MudBlazor css.
```razor
    <!-- deleted: <link rel="preconnect" href="https://fonts.googleapis.com" /> -->
    <!-- deleted: <link href="https://fonts.googleapis.com/css?family=Roboto..." /> -->
```
(Leave no dead markup — actually delete both lines.)

- [ ] **Step 4: Build-verify.**
```bash
dotnet build Nook.sln
```
Expect: `0 Error(s)`.

- [ ] **Step 5: Manual network verify.** `dotnet watch`, open http://localhost:5176 with DevTools → Network, filter `font`, reload. Observe: requests to `/fonts/*.woff2` (200), and ZERO requests to `fonts.googleapis.com` / `fonts.gstatic.com`. In Console: `document.fonts.check('16px "Figtree"')` returns `true` after load.

- [ ] **Step 6: Commit.**
```bash
git add wwwroot/fonts/*.woff2 wwwroot/css/nook-tokens.css Components/App.razor
git commit -m "feat(visual): self-host Bricolage/Figtree/JetBrains Mono, drop Google Fonts"
```

---

### Task 9: Add NodeUi.KindAccent / KindAccentVar (pure C#, xUnit TDD)

*Stream: VISUAL-SYSTEM · orderHint 32 · Depends on: none*

> **Reconciliation:** duplicates Task 4 (backend-additive) — identical 16-kind hex map, identical `--kind-<lowercase>` derivation, and the same `Nook.Tests/NodeUiTests.cs` target. If Task 4 has landed, this becomes a verify/no-op; keep exactly one copy of the two methods and one `NodeUiTests.cs`. The hexes here equal the `--kind-*` tokens in `nook-tokens.css` (Task 7).

**Files:**
- Modify: `Services/NodeUi.cs` (add two static methods; keep existing Icon/StateColor untouched)
- Test: `Nook.Tests/NodeUiTests.cs` (new xUnit class — PURE logic, no DB harness needed; mirror the `using`/namespace of `Nook.Tests/NodeServiceTests.cs`)

**Interfaces:**
- Produces on `Nook.Services.NodeUi`:
  - `public static string KindAccent(NodeKind kind)` → hex string from the 16-kind map (e.g. `NodeKind.Note` → `"#4C7DF0"`), default arm returns Unclassified `"#8C8578"`.
  - `public static string KindAccentVar(NodeKind kind)` → the CSS var NAME, `"--kind-" + kind.ToString().ToLowerInvariant()` (e.g. `NodeKind.Note` → `"--kind-note"`, `NodeKind.Organization` → `"--kind-organization"`).
- Consumes: `Nook.Models.NodeKind` (existing). The hex values MUST equal the `--kind-*` values in `wwwroot/css/nook-tokens.css`. `ObjectTypeBadge` and `NookTheme` consume both methods.

**Steps:**

- [ ] **Step 1: Failing test first.** Create `Nook.Tests/NodeUiTests.cs`. Pure static — no `GraphHarness`. Assert a representative sample of the hex map, the default arm, and the var-name derivation (including a multi-word kind).
```csharp
using Nook.Models;
using Nook.Services;
using Xunit;

namespace Nook.Tests;

public class NodeUiTests
{
    [Theory]
    [InlineData(NodeKind.Unclassified, "#8C8578")]
    [InlineData(NodeKind.Note, "#4C7DF0")]
    [InlineData(NodeKind.Idea, "#F5B417")]
    [InlineData(NodeKind.Project, "#7C5CFF")]
    [InlineData(NodeKind.Event, "#EE5D9A")]
    public void KindAccent_returns_spec_hex(NodeKind kind, string hex)
        => Assert.Equal(hex, NodeUi.KindAccent(kind));

    [Theory]
    [InlineData(NodeKind.Note, "--kind-note")]
    [InlineData(NodeKind.Organization, "--kind-organization")]
    [InlineData(NodeKind.Unclassified, "--kind-unclassified")]
    public void KindAccentVar_derives_lowercase_var(NodeKind kind, string var)
        => Assert.Equal(var, NodeUi.KindAccentVar(kind));

    [Fact]
    public void KindAccent_covers_every_enum_member()
    {
        foreach (NodeKind k in Enum.GetValues<NodeKind>())
            Assert.StartsWith("#", NodeUi.KindAccent(k));
    }
}
```
Run (expect COMPILE FAILURE — methods don't exist yet):
```bash
dotnet test Nook.sln --filter "FullyQualifiedName~NodeUiTests"
```

- [ ] **Step 2: Minimal impl in `Services/NodeUi.cs`.** Add both methods (do not disturb existing members).
```csharp
    public static string KindAccent(NodeKind kind) => kind switch
    {
        NodeKind.Unclassified => "#8C8578",
        NodeKind.Note => "#4C7DF0",
        NodeKind.Journal => "#E0863C",
        NodeKind.Observation => "#14B8A6",
        NodeKind.Idea => "#F5B417",
        NodeKind.Reference => "#5878A6",
        NodeKind.Bookmark => "#F2416A",
        NodeKind.List => "#23A968",
        NodeKind.Person => "#F76F5A",
        NodeKind.Project => "#7C5CFF",
        NodeKind.Place => "#A15C34",
        NodeKind.Organization => "#A24C8C",
        NodeKind.Topic => "#17B0C4",
        NodeKind.Resource => "#7E9C24",
        NodeKind.Collection => "#B78430",
        NodeKind.Event => "#EE5D9A",
        _ => "#8C8578",
    };

    public static string KindAccentVar(NodeKind kind) =>
        "--kind-" + kind.ToString().ToLowerInvariant();
```

- [ ] **Step 3: Green.**
```bash
dotnet test Nook.sln --filter "FullyQualifiedName~NodeUiTests"
```
Expect: `Passed!  - Failed: 0`.

- [ ] **Step 4: Commit.**
```bash
git add Services/NodeUi.cs Nook.Tests/NodeUiTests.cs
git commit -m "feat(visual): NodeUi.KindAccent/KindAccentVar with xUnit coverage"
```

---

### Task 10: Add NookTheme static MudTheme factory

*Stream: VISUAL-SYSTEM · orderHint 33 · Depends on: Task 7 (nook-tokens.css), Task 8 (fonts)*

> **Reconciliation:** this is the canonical `Services/NookTheme.cs`. Task 14 (WorkspaceShell) also lists creating it — that step is superseded; WorkspaceShell reuses `NookTheme.Build()` produced here. Font-family names and hexes agree with Task 8 / Task 7.

**Files:**
- Create: `Services/NookTheme.cs` (namespace `Nook.Services`; static factory returning a MudBlazor `MudTheme`)
- Modify: `Components/Layout/MainLayout.razor:49` (replace `new MudTheme()` with `NookTheme.Build()`) so the existing shell picks up the palette immediately; the future `WorkspaceShell` will reuse the same factory
- Test: none required (pure construction, no branching logic). If you extract a helper with real branching, add an xUnit test; otherwise build-verify only.

**Interfaces:**
- Produces: `public static class NookTheme` with `public static MudTheme Build()` returning a `MudTheme` whose `PaletteLight`/`PaletteDark` mirror the contract hexes (Primary = Iris `#5B54E8`, Secondary = Tangerine `#F98A3C`, light Background `#F7F5F0` / Surface `#FFFFFF` / TextPrimary `#262119` / lines `#E9E4D9`; dark Background `#16130E` / Surface `#201C15` / TextPrimary `#F3EEE4`), `LayoutProperties.DefaultBorderRadius = "10px"`, and `Typography` families set to `"Figtree"` (default/body), `"Bricolage Grotesque"` (H1-H6), `"JetBrains Mono"` (mono) — the exact `font-family` names produced by the fonts task.
- Consumes: font family names from the fonts task; hexes agree with `nook-tokens.css`. `MainLayout` and the later `WorkspaceShell` bind `MudThemeProvider Theme` to `NookTheme.Build()`.

**Steps:**

- [ ] **Step 1: Create `Services/NookTheme.cs`.** Mirror the palette. Use MudBlazor v9 palette types (`PaletteLight`/`PaletteDark`). Set fonts as string arrays.
```csharp
using MudBlazor;

namespace Nook.Services;

/// <summary>Static MudTheme factory mirroring the Nook token palette.</summary>
public static class NookTheme
{
    private static readonly string[] Body = { "Figtree", "system-ui", "sans-serif" };
    private static readonly string[] Display = { "Bricolage Grotesque", "system-ui", "sans-serif" };
    private static readonly string[] Mono = { "JetBrains Mono", "ui-monospace", "monospace" };

    public static MudTheme Build() => new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#5B54E8",
            Secondary = "#F98A3C",
            Background = "#F7F5F0",
            Surface = "#FFFFFF",
            AppbarBackground = "#FFFFFF",
            DrawerBackground = "#F7F5F0",
            TextPrimary = "#262119",
            LinesDefault = "#E9E4D9",
            TableLines = "#E9E4D9",
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#5B54E8",
            Secondary = "#F98A3C",
            Background = "#16130E",
            Surface = "#201C15",
            AppbarBackground = "#201C15",
            DrawerBackground = "#16130E",
            TextPrimary = "#F3EEE4",
            LinesDefault = "#2C271E",
        },
        LayoutProperties = new LayoutProperties { DefaultBorderRadius = "10px" },
        Typography = new Typography
        {
            Default = new DefaultTypography { FontFamily = Body },
            H1 = new H1Typography { FontFamily = Display },
            H2 = new H2Typography { FontFamily = Display },
            H3 = new H3Typography { FontFamily = Display },
            H4 = new H4Typography { FontFamily = Display },
            H5 = new H5Typography { FontFamily = Display },
            H6 = new H6Typography { FontFamily = Display },
            Body1 = new Body1Typography { FontFamily = Body },
            Body2 = new Body2Typography { FontFamily = Body },
            Button = new ButtonTypography { FontFamily = Body },
            Caption = new CaptionTypography { FontFamily = Body },
        },
    };
}
```
(If a MudBlazor v9 typography property name differs at build time, fix to the compiler's suggestion — the palette hexes and radius are the load-bearing part.)

- [ ] **Step 2: Point `MainLayout.razor` at the factory.** Replace line 49 `private readonly MudTheme _theme = new();` with:
```csharp
    private readonly MudTheme _theme = NookTheme.Build();
```
And add `@using Nook.Services` at the top of the file if not already imported (check `_Imports.razor` first; likely already global).

- [ ] **Step 3: Build-verify.**
```bash
dotnet build Nook.sln
```
Expect: `0 Error(s)`. If typography member names error, adjust to the MudBlazor v9 API and rebuild.

- [ ] **Step 4: Manual verify.** `dotnet watch`, open http://localhost:5176. Observe: app chrome uses Iris-tinted primary buttons/appbar, body copy renders in Figtree (not Roboto/Helvetica), headings in Bricolage Grotesque, and the dark-mode toggle flips to the `#16130E` canvas.

- [ ] **Step 5: Commit.**
```bash
git add Services/NookTheme.cs Components/Layout/MainLayout.razor
git commit -m "feat(visual): NookTheme MudTheme factory with Iris/Sand palette + fonts"
```

---

### Task 11: Add ObjectTypeBadge component (kind color dot + icon/label)

*Stream: VISUAL-SYSTEM · orderHint 34 · Depends on: Task 7 (nook-tokens.css), Task 9 (NodeUi.KindAccent — also produced by Task 4)*

> **Reconciliation:** canonical owner of `Components/Shared/ObjectTypeBadge.razor` (contract: `Kind` + `ShowLabel` + `Size`). Task 24 (Node Page) lists creating this same file — that is superseded; Node-Page consumers use THIS component. To also serve `BacklinksPanel` (Task 26), expose an optional `Dense` (bool=false) alias alongside `ShowLabel`/`Size`. All consumers (Tasks 16, 24, 26) bind against this one component.

**Files:**
- Create: `Components/Shared/ObjectTypeBadge.razor`
- Test: none — Blazor component, no bUnit harness in the spine. Build-verify + a temporary manual drop on a page.

**Interfaces:**
- Produces component `<ObjectTypeBadge Kind="NodeKind" ShowLabel="bool" Size="Size" />`:
  - `[Parameter] public NodeKind Kind { get; set; }`
  - `[Parameter] public bool ShowLabel { get; set; } = true;`
  - `[Parameter] public Size Size { get; set; } = Size.Small;` (MudBlazor `Size`)
  - Renders a colored dot using `var(NodeUi.KindAccentVar(Kind))` (falls back to `NodeUi.KindAccent(Kind)` hex so it also works outside a token scope), the `NodeUi.Icon(Kind)`, and, when `ShowLabel`, the kind name.
- Consumes: `Nook.Services.NodeUi` (`Icon`, `KindAccent`, `KindAccentVar`), `Nook.Models.NodeKind`, MudBlazor `Size`. Consumed later by `SidebarNodeLink`, `NodePage`, `Breadcrumbs`.

**Steps:**

- [ ] **Step 1: Create `Components/Shared/ObjectTypeBadge.razor`.** Inline-flex layout; dot colored from the kind var with hex fallback so it renders even before tokens cascade.
```razor
@using Nook.Models
@using Nook.Services

<span class="nook-otb nook-otb--@Size.ToString().ToLowerInvariant()" title="@Kind">
    <span class="nook-otb__dot"
          style="background: var(@NodeUi.KindAccentVar(Kind), @NodeUi.KindAccent(Kind));"></span>
    <MudIcon Icon="@NodeUi.Icon(Kind)" Size="Size" Style="@($"color: var({NodeUi.KindAccentVar(Kind)}, {NodeUi.KindAccent(Kind)});")" />
    @if (ShowLabel)
    {
        <span class="nook-otb__label">@Kind.ToString()</span>
    }
</span>

@code {
    [Parameter] public NodeKind Kind { get; set; }
    [Parameter] public bool ShowLabel { get; set; } = true;
    [Parameter] public Size Size { get; set; } = Size.Small;
}
```

- [ ] **Step 2: Add scoped styles** in a collocated `Components/Shared/ObjectTypeBadge.razor.css` (Blazor scoped CSS, auto-bundled into `Nook.styles.css`).
```css
.nook-otb { display: inline-flex; align-items: center; gap: var(--space-2); font-family: var(--font-body); color: var(--ink); }
.nook-otb__dot { width: 8px; height: 8px; border-radius: 50%; flex: 0 0 auto; }
.nook-otb--small .nook-otb__label { font-size: 0.8125rem; }
.nook-otb__label { line-height: 1; }
```

- [ ] **Step 3: Build-verify.**
```bash
dotnet build Nook.sln
```
Expect: `0 Error(s)`.

- [ ] **Step 4: Manual verify (temporary drop).** Temporarily add to `Components/Pages/Home.razor` (or any existing page) a row of badges, run `dotnet watch`, open http://localhost:5176:
```razor
<div style="display:flex; gap:16px; flex-wrap:wrap">
    @foreach (var k in Enum.GetValues<Nook.Models.NodeKind>())
    {
        <Nook.Components.Shared.ObjectTypeBadge Kind="k" />
    }
</div>
```
Observe: 16 badges, each with a distinct colored dot matching its kind accent (Note=blue, Idea=amber, Event=pink, etc.), the correct Material icon, and the kind label. Then REMOVE the temporary block.

- [ ] **Step 5: Commit** (after removing the scratch markup).
```bash
git add Components/Shared/ObjectTypeBadge.razor Components/Shared/ObjectTypeBadge.razor.css
git commit -m "feat(visual): ObjectTypeBadge kind dot + icon/label component"
```

---

### Task 12: Add theme-interop collocated module (setTheme on `<html data-theme>`)

*Stream: VISUAL-SYSTEM · orderHint 35 · Depends on: Task 7 (nook-tokens.css)*

> **Reconciliation:** the standalone `/js/theme-interop.js` (`setTheme(name)`) is the early-paint / token-layer form. The WIRED production switch is the global `window.__nookTheme.setTheme(isDark)` registered by `WorkspaceShell.razor.js` (Task 14) and called by `ThemeState.SetAsync` (Task 13) — same `<html data-theme>` effect. Either can back `ThemeState`; the global is the one the shipped code invokes.

**Files:**
- Create: `Components/Layout/WorkspaceShell.razor.js` is owned by the shell stream; to avoid a cross-stream file collision, put theme interop in its own module: `wwwroot/js/theme-interop.js` (ES module, imported by `ThemeState` in band 40+ via `import("/js/theme-interop.js")`).
- Test: none — trivial DOM interop. Build-verify + manual console check (mirrors the existing static-module precedent `Components/Layout/ReconnectModal.razor.js`).

**Interfaces:**
- Produces ES module at stable URL `/js/theme-interop.js` exporting `export function setTheme(name)` which sets `document.documentElement.dataset.theme = name` (and, when `name` is null/empty, removes the attribute so light default applies).
- Consumes: nothing. `Services/ThemeState.SetAsync` (band 40+) resolves it via `JS.InvokeAsync<IJSObjectReference>("import", "/js/theme-interop.js")` then calls `module.InvokeVoidAsync("setTheme", isDark ? "dark" : null)`. Pairs with the `[data-theme="dark"]` block defined in `nook-tokens.css`.

**Steps:**

- [ ] **Step 1: Create `wwwroot/js/theme-interop.js`.** Plain ES module (no bundling), served statically at `/js/theme-interop.js`.
```js
// theme-interop.js — flips <html data-theme> for the Nook token layer.
export function setTheme(name) {
    const root = document.documentElement;
    if (name) {
        root.dataset.theme = name;
    } else {
        delete root.dataset.theme;
    }
}
```

- [ ] **Step 2: Build-verify.**
```bash
dotnet build Nook.sln
```
Expect: `0 Error(s)` (module is a static asset; this just confirms nothing else broke).

- [ ] **Step 3: Manual interop verify.** `dotnet watch`, open http://localhost:5176, DevTools console:
```js
const m = await import('/js/theme-interop.js');
m.setTheme('dark');   document.documentElement.dataset.theme; // 'dark'
getComputedStyle(document.documentElement).getPropertyValue('--canvas'); // ' #16130E'
m.setTheme(null);     document.documentElement.hasAttribute('data-theme'); // false
```
Observe: `setTheme('dark')` sets the attribute and the `--canvas` token resolves to the dark hex; `setTheme(null)` removes it and light returns. This confirms the interop + token layer compose before `ThemeState` (band 40+) wires it to persistence.

- [ ] **Step 4: Commit.**
```bash
git add wwwroot/js/theme-interop.js
git commit -m "feat(visual): theme-interop module for html data-theme switching"
```

---

### Task 13: Add WorkspaceState + ThemeState scoped services

*Stream: WORKSPACE-SHELL · orderHint 40 · Depends on: Task 5 (UserPreference model + IUserPreferenceService)*

> **Reconciliation:** `IUserPreferenceService` signatures match Task 5 exactly; the Node PK is `Node.NodeId` (backend/schema owner) — the `n.Id` projection in `HydrateAsync` reads `n.NodeId` (`RecentNode.Id` is the record's own field); `ThemeState.SetAsync` calls the global `__nookTheme.setTheme(bool)` registered by `WorkspaceShell.razor.js` (Task 14), equivalent to the `theme-interop.js` module (Task 12).

**Files:**
- Create: `Services/WorkspaceState.cs` (namespace `Nook.Services`)
- Create: `Services/ThemeState.cs` (namespace `Nook.Services`)
- Modify: `Program.cs:73` — add two `AddScoped` lines inside the graph-services block (after `AddScoped<IGraphMigrationService, GraphMigrationService>()`)
- Test: `Nook.Tests/WorkspaceStateTests.cs` (pure `MergeRecentIds` MRU logic only)

**Interfaces:**
_Consumes_ (from BACKEND stream, must already exist):
- `IUserPreferenceService.GetRecentIdsAsync(CancellationToken=default) : Task<IReadOnlyList<int>>`
- `IUserPreferenceService.PushRecentAsync(int nodeId) : Task`
- `IUserPreferenceService.SetDarkModeAsync(bool on) : Task`
- `IUserPreferenceService.GetOrCreateAsync(CancellationToken=default) : Task<UserPreference>` (for `IsDarkMode` seed)
- `INodeService.GetByIdAsync(int id) : Task<Node?>` (hydrate recents)

_Produces_ (other shell tasks consume these EXACT shapes):
- `public sealed record RecentNode(int Id, string Title, Nook.Models.NodeKind Kind);`
- `WorkspaceState` (scoped): `IReadOnlyList<RecentNode> Recents { get; }`, `IReadOnlyList<Breadcrumb> Trail { get; }`, `event Action? Changed`, `Task InitializeAsync()`, `Task NoteVisitedAsync(int nodeId)`, `void SetTrail(IReadOnlyList<Breadcrumb> trail)`, and PURE static `static IReadOnlyList<int> MergeRecentIds(IReadOnlyList<int> current, int visitedId, int cap = 12)`
- `public sealed record Breadcrumb(string Label, string? Href);`
- `ThemeState` (scoped): `bool IsDarkMode { get; }`, `event Action? Changed`, `Task InitializeAsync()`, `Task SetAsync(bool on)`

**Steps:**

- [ ] **Step 1: Branch off main.**
```bash
cd C:/Users/capnb/source/repos/Nook && git checkout main && git pull --ff-only && git checkout -b feature/workspace-shell
```
Expect: `Switched to a new branch 'feature/workspace-shell'`.

- [ ] **Step 2: Write the FAILING pure-logic test first (TDD).** The only pure logic in WorkspaceState is the recents-merge (dedupe + move-to-front + cap). Read `Nook.Tests/GraphHarness.cs` first (already reviewed: xUnit, no DI). This test needs NO harness — it exercises a pure static method. Create `Nook.Tests/WorkspaceStateTests.cs`:
```csharp
using Nook.Services;
using Xunit;

namespace Nook.Tests;

public class WorkspaceStateTests
{
    [Fact]
    public void MergeRecentIds_MovesVisitedToFront_AndDedupes()
    {
        var result = WorkspaceState.MergeRecentIds(new[] { 3, 1, 2 }, 2);
        Assert.Equal(new[] { 2, 3, 1 }, result);
    }

    [Fact]
    public void MergeRecentIds_CapsAtTwelve()
    {
        var current = Enumerable.Range(1, 12).ToArray(); // 1..12
        var result = WorkspaceState.MergeRecentIds(current, 99);
        Assert.Equal(12, result.Count);
        Assert.Equal(99, result[0]);
        Assert.DoesNotContain(12, result); // oldest evicted
    }

    [Fact]
    public void MergeRecentIds_NewId_PrependsWithoutDuplicating()
    {
        var result = WorkspaceState.MergeRecentIds(new[] { 5, 6 }, 7);
        Assert.Equal(new[] { 7, 5, 6 }, result);
    }
}
```
Run it — must FAIL to compile (method not yet defined):
```bash
dotnet test Nook.sln --filter "FullyQualifiedName~WorkspaceStateTests"
```
Expect: build error `'WorkspaceState' does not contain a definition for 'MergeRecentIds'`. That is the red state.

- [ ] **Step 3: Create `Services/WorkspaceState.cs` (minimal impl to pass).**
```csharp
using Nook.Models;

namespace Nook.Services;

public sealed record RecentNode(int Id, string Title, NodeKind Kind);
public sealed record Breadcrumb(string Label, string? Href);

/// <summary>Scoped UI state: MRU recents + current breadcrumb trail. Cascaded from WorkspaceShell.</summary>
public sealed class WorkspaceState
{
    private readonly IUserPreferenceService _prefs;
    private readonly INodeService _nodes;
    private List<int> _recentIds = new();

    public WorkspaceState(IUserPreferenceService prefs, INodeService nodes)
    {
        _prefs = prefs;
        _nodes = nodes;
    }

    public IReadOnlyList<RecentNode> Recents { get; private set; } = Array.Empty<RecentNode>();
    public IReadOnlyList<Breadcrumb> Trail { get; private set; } = Array.Empty<Breadcrumb>();
    public event Action? Changed;

    /// <summary>PURE: dedupe, move visited id to front, cap. Unit-tested.</summary>
    public static IReadOnlyList<int> MergeRecentIds(IReadOnlyList<int> current, int visitedId, int cap = 12)
    {
        var list = new List<int>(current.Count + 1) { visitedId };
        foreach (var id in current)
            if (id != visitedId) list.Add(id);
        if (list.Count > cap) list.RemoveRange(cap, list.Count - cap);
        return list;
    }

    public async Task InitializeAsync()
    {
        _recentIds = (await _prefs.GetRecentIdsAsync()).ToList();
        await HydrateAsync();
    }

    public async Task NoteVisitedAsync(int nodeId)
    {
        await _prefs.PushRecentAsync(nodeId);
        _recentIds = MergeRecentIds(_recentIds, nodeId).ToList();
        await HydrateAsync();
        Changed?.Invoke();
    }

    public void SetTrail(IReadOnlyList<Breadcrumb> trail)
    {
        Trail = trail;
        Changed?.Invoke();
    }

    private async Task HydrateAsync()
    {
        var hydrated = new List<RecentNode>(_recentIds.Count);
        foreach (var id in _recentIds)
        {
            var n = await _nodes.GetByIdAsync(id);
            if (n is not null) hydrated.Add(new RecentNode(n.Id, n.Title, n.Kind));
        }
        Recents = hydrated;
    }
}
```
Note: `Node` is assumed to expose `Id`, `Title`, `Kind` (confirmed via NodeUi usage). Per the reconciliation above, the real PK is `Node.NodeId` — read `n.NodeId` in the `RecentNode` projection if the model has no `Id` alias.

- [ ] **Step 4: Green the test.**
```bash
dotnet test Nook.sln --filter "FullyQualifiedName~WorkspaceStateTests"
```
Expect: `Passed!  - Failed: 0, Passed: 3`.

- [ ] **Step 5: Create `Services/ThemeState.cs`.** SetAsync persists via prefs AND calls the theme-interop JS global (`__nookTheme.setTheme`, registered by WorkspaceShell.razor.js in the next task) so `<html data-theme>` flips even before the Mud re-render.
```csharp
using Microsoft.JSInterop;

namespace Nook.Services;

/// <summary>Scoped dark/light state. MudThemeProvider.IsDarkMode binds here; persists to UserPreference.</summary>
public sealed class ThemeState
{
    private readonly IUserPreferenceService _prefs;
    private readonly IJSRuntime _js;

    public ThemeState(IUserPreferenceService prefs, IJSRuntime js)
    {
        _prefs = prefs;
        _js = js;
    }

    public bool IsDarkMode { get; private set; }
    public event Action? Changed;

    public async Task InitializeAsync()
    {
        var pref = await _prefs.GetOrCreateAsync();
        IsDarkMode = pref.IsDarkMode;
        Changed?.Invoke();
    }

    public async Task SetAsync(bool on)
    {
        if (on == IsDarkMode) return;
        IsDarkMode = on;
        await _prefs.SetDarkModeAsync(on);
        try { await _js.InvokeVoidAsync("__nookTheme.setTheme", on); }
        catch (JSDisconnectedException) { }
        Changed?.Invoke();
    }
}
```

- [ ] **Step 6: Register both in `Program.cs`** right after the `AddScoped<IGraphMigrationService, GraphMigrationService>();` line (Program.cs:76):
```csharp
// Workspace shell UI state.
builder.Services.AddScoped<WorkspaceState>();
builder.Services.AddScoped<ThemeState>();
```

- [ ] **Step 7: Build the whole solution.**
```bash
dotnet build Nook.sln
```
Expect: `Build succeeded. 0 Error(s)`. (WorkspaceState/ThemeState will be consumed by WorkspaceShell in the next task.)

- [ ] **Step 8: Commit.**
```bash
git add Services/WorkspaceState.cs Services/ThemeState.cs Program.cs Nook.Tests/WorkspaceStateTests.cs && git commit -m "feat(shell): WorkspaceState (MRU recents) + ThemeState scoped services"
```

---

### Task 14: Build WorkspaceShell layout + collocated shortcuts/theme JS

*Stream: WORKSPACE-SHELL · orderHint 41 · Depends on: Task 13 (WorkspaceState + ThemeState), Task 7 (nook-tokens.css) + Task 8 (fonts)*

> **Reconciliation:** `Services/NookTheme.cs` is already produced by Task 10 — reuse `NookTheme.Build()`; Step 1 here is a fallback only if Task 10 hasn't merged. The `<CommandPalette>`/`OpenPalette` wiring is a placeholder that Task 19 finalizes with bound `Open`/`OpenChanged`/`Actions` and a `[JSInvokable] OpenPalette`; the palette's imperative open method is `Show()` (Task 19), not `Open()`. `GlobalRail`/`WorkspaceSidebar`/`TopBar` land in Tasks 15-16.

**Files:**
- Create: `Components/Layout/WorkspaceShell.razor` (`@inherits LayoutComponentBase`)
- Create: `Components/Layout/WorkspaceShell.razor.js` (collocated module: shortcuts + theme-interop)
- Create: `Services/NookTheme.cs` (static `MudTheme` factory; may be refined later by DESIGN stream)
- Create: `Components/Layout/WorkspaceShell.razor.css` (CSS-grid scaffold; uses `nook-tokens.css` custom props with fallbacks)
- Modify: (none yet — Routes.razor swap happens in task 5)

**Interfaces:**
_Consumes_:
- `WorkspaceState` (Initialize, cascaded), `ThemeState` (Initialize/IsDarkMode/SetAsync/Changed) from task 40
- `NookTheme.Build() : MudTheme` (created here)
- Placeholder child components rendered but owned by other streams: `<GlobalRail/>`, `<WorkspaceSidebar/>`, `<TopBar/>` (tasks 42/43), `<CommandPalette @ref/>` (PALETTE stream)

_Produces_:
- `CascadingValue<WorkspaceState>` wrapping `@Body` (all descendant pages/panels read it)
- JS module `Components/Layout/WorkspaceShell.razor.js` exports: `initialize(dotNetRef)`, `dispose()`, and registers `window.__nookTheme = { setTheme(isDark) }`
- `[JSInvokable] Task OnShortcutAsync(string combo)` on WorkspaceShell (e.g. "mod+k", "g h") — opens palette / navigates
- `NookTheme` (static): `public static MudTheme Build()`

**Steps:**

- [ ] **Step 1: Create `Services/NookTheme.cs`** — a static MudTheme factory seeded from the brand tokens (Iris #5B54E8, Tangerine #F98A3C, Sand canvas/ink). Keep palette values inline; DESIGN stream may later point these at CSS vars. **(Reconciliation: if Task 10 already created this file, SKIP this step and reuse `NookTheme.Build()`.)**
```csharp
using MudBlazor;

namespace Nook.Services;

public static class NookTheme
{
    public static MudTheme Build() => new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#5B54E8",          // Iris
            Secondary = "#F98A3C",        // Tangerine
            Background = "#F7F5F0",       // Sand canvas
            Surface = "#FFFFFF",          // card
            TextPrimary = "#262119",      // ink
            LinesDefault = "#E9E4D9",     // line
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#5B54E8",
            Secondary = "#F98A3C",
            Background = "#16130E",       // dark canvas
            Surface = "#201C15",          // dark surface
            TextPrimary = "#F3EEE4",      // dark ink
        },
        Typography = new Typography
        {
            Default = new DefaultTypography { FontFamily = new[] { "Figtree", "sans-serif" } },
            H1 = new H1Typography { FontFamily = new[] { "Bricolage Grotesque", "serif" } },
            H2 = new H2Typography { FontFamily = new[] { "Bricolage Grotesque", "serif" } },
        },
    };
}
```
Note: MudBlazor v9 palette/typography property names — if the build flags a renamed member (e.g. `LinesDefault`), read `_content/MudBlazor` intellisense or an existing theme and correct in place. Confirm names before finalizing.

- [ ] **Step 2: Create the collocated JS module `Components/Layout/WorkspaceShell.razor.js`.** Global keydown → .NET; suppress single-letter chords while typing in inputs/textareas/contenteditable; always allow mod+k.
```javascript
let _ref = null;
let _handler = null;
let _chordFirst = null;
let _chordTimer = null;

function isEditing(t) {
  if (!t) return false;
  const tag = t.tagName;
  return tag === 'INPUT' || tag === 'TEXTAREA' || t.isContentEditable;
}

export function initialize(dotNetRef) {
  _ref = dotNetRef;
  window.__nookTheme = {
    setTheme(isDark) {
      document.documentElement.setAttribute('data-theme', isDark ? 'dark' : 'light');
    }
  };
  _handler = (e) => {
    const mod = e.metaKey || e.ctrlKey;
    if (mod && e.key.toLowerCase() === 'k') {
      e.preventDefault();
      _ref.invokeMethodAsync('OnShortcutAsync', 'mod+k');
      return;
    }
    if (isEditing(e.target)) { _chordFirst = null; return; }
    // simple two-key 'g h' style chord
    if (_chordFirst) {
      const combo = `${_chordFirst} ${e.key.toLowerCase()}`;
      _chordFirst = null;
      clearTimeout(_chordTimer);
      _ref.invokeMethodAsync('OnShortcutAsync', combo);
      return;
    }
    if (e.key.toLowerCase() === 'g') {
      _chordFirst = 'g';
      _chordTimer = setTimeout(() => { _chordFirst = null; }, 800);
    }
  };
  document.addEventListener('keydown', _handler);
}

export function dispose() {
  if (_handler) document.removeEventListener('keydown', _handler);
  _handler = null; _ref = null; _chordFirst = null;
  clearTimeout(_chordTimer);
}
```

- [ ] **Step 3: Create `Components/Layout/WorkspaceShell.razor.css`** — CSS-grid: rail (fixed) | sidebar | (topbar over content). Uses token vars with hard fallbacks so it renders even before nook-tokens.css lands.
```css
.nook-shell {
    display: grid;
    grid-template-columns: 56px var(--sidebar-w, 260px) 1fr;
    grid-template-rows: 100vh;
    background: var(--nook-canvas, #F7F5F0);
    color: var(--nook-ink, #262119);
}
.nook-shell.sidebar-collapsed { grid-template-columns: 56px 0 1fr; }
.nook-rail { grid-column: 1; border-right: 1px solid var(--nook-line, #E9E4D9); }
.nook-sidebar { grid-column: 2; overflow-y: auto; border-right: 1px solid var(--nook-line, #E9E4D9); }
.nook-main { grid-column: 3; display: grid; grid-template-rows: 52px 1fr; min-width: 0; }
.nook-topbar { grid-row: 1; border-bottom: 1px solid var(--nook-line, #E9E4D9); }
.nook-content { grid-row: 2; overflow-y: auto; padding: 24px; }
```

- [ ] **Step 4: Create `Components/Layout/WorkspaceShell.razor`.** Keep the 4 Mud providers; bind MudThemeProvider to ThemeState; cascade WorkspaceState; render rail/sidebar/topbar/content grid; load the JS module in OnAfterRenderAsync(firstRender). For the spine, reference `<GlobalRail/>`, `<WorkspaceSidebar/>`, `<TopBar/>`, `<CommandPalette @ref>` — these are created in later tasks/streams; until they exist the shell will not compile, so gate this task's build behind tasks 42-43 (see dependsOn) OR temporarily stub the three child tags as empty `<div/>` and replace when the components land. Draft the full version:
```razor
@inherits LayoutComponentBase
@implements IAsyncDisposable
@inject ThemeState Theme
@inject WorkspaceState Workspace
@inject IJSRuntime JS

<MudThemeProvider Theme="_theme" IsDarkMode="Theme.IsDarkMode" />
<MudPopoverProvider />
<MudDialogProvider />
<MudSnackbarProvider />

<CascadingValue Value="Workspace">
    <div class="nook-shell">
        <GlobalRail class="nook-rail" />
        <WorkspaceSidebar class="nook-sidebar" />
        <div class="nook-main">
            <TopBar class="nook-topbar" OnOpenPalette="OpenPalette" />
            <main class="nook-content">@Body</main>
        </div>
    </div>
    <CommandPalette @ref="_palette" />
</CascadingValue>

<div id="blazor-error-ui" data-nosnippet>
    An unhandled error has occurred.
    <a href="." class="reload">Reload</a>
    <span class="dismiss">🗙</span>
</div>

@code {
    private readonly MudTheme _theme = NookTheme.Build();
    private IJSObjectReference? _module;
    private DotNetObjectReference<WorkspaceShell>? _dotNet;
    private CommandPalette? _palette;

    [Inject] private NavigationManager Nav { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        await Theme.InitializeAsync();
        await Workspace.InitializeAsync();
        Theme.Changed += OnStateChanged;
        Workspace.Changed += OnStateChanged;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        _module = await JS.InvokeAsync<IJSObjectReference>(
            "import", "./Components/Layout/WorkspaceShell.razor.js");
        _dotNet = DotNetObjectReference.Create(this);
        await _module.InvokeVoidAsync("initialize", _dotNet);
        // Apply persisted theme to <html> on first paint.
        await JS.InvokeVoidAsync("__nookTheme.setTheme", Theme.IsDarkMode);
    }

    private void OpenPalette() => _palette?.Open();

    [JSInvokable]
    public Task OnShortcutAsync(string combo)
    {
        switch (combo)
        {
            case "mod+k": OpenPalette(); break;
            case "g h": Nav.NavigateTo("/today"); break;
            case "g i": Nav.NavigateTo("/inbox"); break;
        }
        return InvokeAsync(StateHasChanged);
    }

    private void OnStateChanged() => InvokeAsync(StateHasChanged);

    public async ValueTask DisposeAsync()
    {
        Theme.Changed -= OnStateChanged;
        Workspace.Changed -= OnStateChanged;
        if (_module is not null)
        {
            try { await _module.InvokeVoidAsync("dispose"); await _module.DisposeAsync(); }
            catch (JSDisconnectedException) { }
        }
        _dotNet?.Dispose();
    }
}
```
Note the `class` params on child components require each child to declare `[Parameter] public string? Class`. Coordinate: GlobalRail/WorkspaceSidebar/TopBar tasks must accept a pass-through `Class`. If simpler, drop the `class=` attributes here and let each child own its root class name. **(Reconciliation: `OpenPalette` / `_palette?.Open()` here are placeholder; Task 19 replaces them with `_paletteOpen` + bound `<CommandPalette Open OpenChanged Actions>` and a `[JSInvokable] OpenPalette` — the palette's imperative open is `Show()`.)**

- [ ] **Step 5: Build.** This will FAIL until GlobalRail/WorkspaceSidebar/TopBar/CommandPalette exist. Two options: (a) sequence this task AFTER tasks 42-43 + PALETTE stream, or (b) temporarily replace the four child tags with `<div/>` placeholders to prove the shell + JS + theme wiring compiles in isolation, then restore. Prefer (a). Run:
```bash
dotnet build Nook.sln
```
Expect (after children exist): `Build succeeded. 0 Error(s)`.

- [ ] **Step 6: Manual verify (deferred until Routes swap in task 5, but the JS/theme path can be smoke-tested now).** After task 5 wires the DefaultLayout: `dotnet watch` → open http://localhost:5176 → confirm (1) three-column grid renders, (2) pressing Cmd/Ctrl+K fires `OnShortcutAsync` (set a breakpoint or temporary Console.WriteLine), (3) toggling theme flips `<html data-theme>` in devtools Elements panel and Mud surfaces recolor. Note honestly: no bUnit test exists for this component — this is a manual observation step.

- [ ] **Step 7: Commit.**
```bash
git add Components/Layout/WorkspaceShell.razor Components/Layout/WorkspaceShell.razor.js Components/Layout/WorkspaceShell.razor.css Services/NookTheme.cs && git commit -m "feat(shell): WorkspaceShell CSS-grid layout, NookTheme, shortcuts+theme JS"
```

---

### Task 15: Build GlobalRail, TopBar, and Breadcrumbs

*Stream: WORKSPACE-SHELL · orderHint 42 · Depends on: Task 13 (WorkspaceState + ThemeState)*

> **Reconciliation:** `TopBar.OnOpenPalette` (EventCallback) is bound by WorkspaceShell (Task 14) and drives the palette open in Task 19. `ThemeState` here is the Task 13 service. Each child declares `[Parameter] public string? Class` to accept the shell's `class=` pass-through.

**Files:**
- Create: `Components/Workspace/GlobalRail.razor`
- Create: `Components/Workspace/TopBar.razor`
- Create: `Components/Workspace/Breadcrumbs.razor`
- (Optional) collocated `.razor.css` for each

**Interfaces:**
_Consumes_:
- `[CascadingParameter] WorkspaceState Workspace` (reads `Trail` for Breadcrumbs)
- `ThemeState` (injected in TopBar for the light/dark toggle: `IsDarkMode`, `SetAsync`)
- `NavigationManager` for back/forward + rail nav

_Produces_:
- `GlobalRail`: `[Parameter] public string? Class { get; set; }` — fixed icon column (Home /today, Search /search, Pages /all, Canvas stub, Settings, avatar)
- `TopBar`: `[Parameter] public string? Class`, `[Parameter] public EventCallback OnOpenPalette` — back/fwd buttons, `<Breadcrumbs/>`, Cmd+K button (invokes OnOpenPalette), theme toggle
- `Breadcrumbs`: renders `Workspace.Trail` (`IReadOnlyList<Breadcrumb>`), last crumb non-link

**Steps:**

- [ ] **Step 1: Create `Components/Workspace/GlobalRail.razor`.** Fixed vertical icon rail using MudIconButton with Href. Canvas is a disabled stub.
```razor
<div class="@($"nook-rail-inner {Class}")">
    <MudStack AlignItems="AlignItems.Center" Spacing="1" Class="pt-2">
        <MudIconButton Icon="@Icons.Material.Filled.Home" Href="/today" title="Home" />
        <MudIconButton Icon="@Icons.Material.Filled.Search" Href="/search" title="Search" />
        <MudIconButton Icon="@Icons.Material.Filled.Description" Href="/all" title="Pages" />
        <MudIconButton Icon="@Icons.Material.Filled.Draw" Disabled="true" title="Canvas (coming soon)" />
        <MudSpacer />
        <MudIconButton Icon="@Icons.Material.Filled.Settings" Href="/tags" title="Settings" />
        <MudIconButton Icon="@Icons.Material.Filled.AccountCircle" Href="/Account/Manage" title="Account" />
    </MudStack>
</div>
@code {
    [Parameter] public string? Class { get; set; }
}
```
Note: MudSpacer inside a vertical MudStack may not push to bottom; if it doesn't, use `Style="height:100%"` + `justify-content:space-between` on a flex container instead. Verify visually.

- [ ] **Step 2: Create `Components/Workspace/Breadcrumbs.razor`.** Reads the cascaded trail.
```razor
@implements IDisposable
<nav class="nook-breadcrumbs" aria-label="Breadcrumb">
    @if (Workspace.Trail.Count == 0)
    {
        <span class="mud-text-secondary">Nook</span>
    }
    else
    {
        @for (var i = 0; i < Workspace.Trail.Count; i++)
        {
            var crumb = Workspace.Trail[i];
            var isLast = i == Workspace.Trail.Count - 1;
            if (isLast || crumb.Href is null)
            {
                <span class="crumb crumb-current">@crumb.Label</span>
            }
            else
            {
                <MudLink Href="@crumb.Href" Class="crumb">@crumb.Label</MudLink>
                <span class="crumb-sep">/</span>
            }
        }
    }
</nav>
@code {
    [CascadingParameter] public WorkspaceState Workspace { get; set; } = default!;
    protected override void OnInitialized() => Workspace.Changed += OnChanged;
    private void OnChanged() => InvokeAsync(StateHasChanged);
    public void Dispose() => Workspace.Changed -= OnChanged;
}
```

- [ ] **Step 3: Create `Components/Workspace/TopBar.razor`.** Back/fwd via JS history, breadcrumbs, palette button, theme toggle bound to ThemeState.
```razor
@implements IDisposable
@inject ThemeState Theme
@inject IJSRuntime JS
<div class="@($"nook-topbar-inner {Class}")">
    <MudIconButton Icon="@Icons.Material.Filled.ArrowBack" Size="Size.Small" title="Back"
                   OnClick="@(() => JS.InvokeVoidAsync("history.back"))" />
    <MudIconButton Icon="@Icons.Material.Filled.ArrowForward" Size="Size.Small" title="Forward"
                   OnClick="@(() => JS.InvokeVoidAsync("history.forward"))" />
    <Breadcrumbs />
    <MudSpacer />
    <MudButton Variant="Variant.Outlined" Size="Size.Small" StartIcon="@Icons.Material.Filled.Search"
               OnClick="OnOpenPalette">⌘K</MudButton>
    <MudIconButton Size="Size.Small"
                   Icon="@(Theme.IsDarkMode ? Icons.Material.Filled.LightMode : Icons.Material.Filled.DarkMode)"
                   title="Toggle light/dark"
                   OnClick="@(() => Theme.SetAsync(!Theme.IsDarkMode))" />
</div>
@code {
    [Parameter] public string? Class { get; set; }
    [Parameter] public EventCallback OnOpenPalette { get; set; }
    protected override void OnInitialized() => Theme.Changed += OnChanged;
    private void OnChanged() => InvokeAsync(StateHasChanged);
    public void Dispose() => Theme.Changed -= OnChanged;
}
```
Layout the `.nook-topbar-inner` as `display:flex; align-items:center; gap:8px; padding:0 12px; height:100%` in a collocated `.razor.css`.

- [ ] **Step 4: Build.**
```bash
dotnet build Nook.sln
```
Expect: `Build succeeded. 0 Error(s)`. (These compile standalone; they only need WorkspaceState/ThemeState from task 40.)

- [ ] **Step 5: Manual verify (after task 5 wires the layout).** `dotnet watch` → http://localhost:5176 → observe: rail icons navigate; back/forward buttons move browser history; the ⌘K button fires OnOpenPalette (palette opens once PALETTE stream lands); the theme toggle icon flips and persists across reload (ThemeState → UserPreference). No component unit test — manual observation only.

- [ ] **Step 6: Commit.**
```bash
git add Components/Workspace/GlobalRail.razor Components/Workspace/TopBar.razor Components/Workspace/Breadcrumbs.razor Components/Workspace/*.razor.css && git commit -m "feat(shell): GlobalRail, TopBar (back/fwd, palette, theme toggle), Breadcrumbs"
```

---

### Task 16: Build WorkspaceSidebar + SidebarNodeLink

*Stream: WORKSPACE-SHELL · orderHint 43 · Depends on: Task 13 (WorkspaceState), Task 11 (ObjectTypeBadge), Task 4 (NodeUi.KindAccent)*

> **Reconciliation:** consumes the canonical `ObjectTypeBadge` (Task 11) with `Kind` and `NodeUi.KindAccent` (Task 4). `n.Id` / `c.Node.Id` read the `Node.NodeId` PK (see Task 13); `r.Id` is `RecentNode.Id`.

**Files:**
- Create: `Components/Workspace/WorkspaceSidebar.razor`
- Create: `Components/Workspace/SidebarNodeLink.razor`

**Interfaces:**
_Consumes_:
- `INodeService.GetFavoritesAsync(int count = 10) : Task<List<Node>>`
- `INodeService.GetPinnedAsync(int count = 10) : Task<List<Node>>`
- `ICollectionService.GetCollectionsAsync(bool includeArchived = false) : Task<List<CollectionSummary>>` (record: `CollectionSummary(Node Node, CollectionKind Kind, bool IsOrdered, int MemberCount)`)
- `ICollectionService.AddMemberAsync(int collectionNodeId, int memberNodeId) : Task<bool>` (drag-drop target)
- `[CascadingParameter] WorkspaceState Workspace` (reads `Recents` → `RecentNode`)
- `ObjectTypeBadge` component (SHARED stream) — props assumed `[Parameter] NodeKind Kind`
- `NodeUi.Icon(NodeKind)`, `NodeUi.KindAccent(NodeKind)` (accent hex, BACKEND stream)

_Produces_:
- `WorkspaceSidebar`: `[Parameter] public string? Class`
- `SidebarNodeLink`: `[Parameter] public int NodeId`, `[Parameter] public string Title`, `[Parameter] public NodeKind Kind` — renders ObjectTypeBadge + title, links to `/nodes/{NodeId}`

**Steps:**

- [ ] **Step 1: Create `Components/Workspace/SidebarNodeLink.razor`.** A single clickable row: badge + title, accent stripe from KindAccent, navigates to the node page.
```razor
@using Nook.Models
<MudNavLink Href="@($"/nodes/{NodeId}")" Class="nook-sidebar-link"
            Style="@($"border-left:3px solid {NodeUi.KindAccent(Kind)}")">
    <div class="d-flex align-center gap-2">
        <ObjectTypeBadge Kind="Kind" />
        <span class="text-truncate">@Title</span>
    </div>
</MudNavLink>
@code {
    [Parameter] public int NodeId { get; set; }
    [Parameter] public string Title { get; set; } = "";
    [Parameter] public NodeKind Kind { get; set; }
}
```
If `ObjectTypeBadge`'s real parameter name differs from `Kind`, adjust after reading the SHARED stream's component. Fallback if ObjectTypeBadge not yet merged: `<MudIcon Icon="@NodeUi.Icon(Kind)" Size="Size.Small" />`.

- [ ] **Step 2: Create `Components/Workspace/WorkspaceSidebar.razor`.** Four sections: Favorites, Pinned, Collections, Recents. Loads favorites/pinned/collections on init; Recents comes live from cascaded WorkspaceState.
```razor
@implements IDisposable
@inject INodeService Nodes
@inject ICollectionService Collections
<div class="@($"nook-sidebar-inner {Class}")">
    <MudNavMenu>
        <div class="nook-sb-heading">Favorites</div>
        @foreach (var n in _favorites)
        {
            <SidebarNodeLink NodeId="n.Id" Title="@n.Title" Kind="n.Kind" />
        }

        <div class="nook-sb-heading">Pinned</div>
        @foreach (var n in _pinned)
        {
            <SidebarNodeLink NodeId="n.Id" Title="@n.Title" Kind="n.Kind" />
        }

        <div class="nook-sb-heading">Collections</div>
        @foreach (var c in _collections)
        {
            <div ondragover="event.preventDefault()" @ondrop="@(() => OnDropOnCollection(c.Node.Id))">
                <MudNavLink Href="@($"/nodes/{c.Node.Id}")" Icon="@Icons.Material.Filled.Collections">
                    @c.Node.Title (@c.MemberCount)
                </MudNavLink>
            </div>
        }

        <div class="nook-sb-heading">Recents</div>
        @foreach (var r in Workspace.Recents)
        {
            <SidebarNodeLink NodeId="r.Id" Title="@r.Title" Kind="r.Kind" />
        }
    </MudNavMenu>
</div>
@code {
    [Parameter] public string? Class { get; set; }
    [CascadingParameter] public WorkspaceState Workspace { get; set; } = default!;
    private List<Node> _favorites = new();
    private List<Node> _pinned = new();
    private List<CollectionSummary> _collections = new();
    private int? _draggedNodeId; // set by SidebarNodeLink drag in follow-up

    protected override async Task OnInitializedAsync()
    {
        _favorites = await Nodes.GetFavoritesAsync();
        _pinned = await Nodes.GetPinnedAsync();
        _collections = await Collections.GetCollectionsAsync();
        Workspace.Changed += OnChanged;
    }

    private async Task OnDropOnCollection(int collectionNodeId)
    {
        if (_draggedNodeId is int nodeId)
        {
            await Collections.AddMemberAsync(collectionNodeId, nodeId);
            _collections = await Collections.GetCollectionsAsync();
            _draggedNodeId = null;
            StateHasChanged();
        }
    }

    private void OnChanged() => InvokeAsync(StateHasChanged);
    public void Dispose() => Workspace.Changed -= OnChanged;
}
```
**Drag wiring decision:** full drag-source wiring (setting `_draggedNodeId` from a `draggable` SidebarNodeLink via a shared drag context) is heavier than the spine needs. For this task, ship the drop TARGET scaffold above (harmless no-op until a source sets `_draggedNodeId`) and mark the drag SOURCE as an explicit follow-up. Keep click-navigation as the primary interaction. Document this in the commit body.

- [ ] **Step 3: Build.**
```bash
dotnet build Nook.sln
```
Expect: `Build succeeded. 0 Error(s)`. Requires ObjectTypeBadge + NodeUi.KindAccent present (dependsOn); if not yet merged, use the MudIcon fallback from Step 1 and a literal color to unblock, then revert.

- [ ] **Step 4: Manual verify (after task 5).** `dotnet watch` → http://localhost:5176 → observe: Favorites/Pinned/Collections populate from real data; each node row shows the kind badge + accent stripe and navigates to `/nodes/{id}`; visiting a node makes it appear at the top of Recents (WorkspaceState.NoteVisitedAsync, wired on NodePage). No component unit test — manual observation.

- [ ] **Step 5: Commit.**
```bash
git add Components/Workspace/WorkspaceSidebar.razor Components/Workspace/SidebarNodeLink.razor && git commit -m "feat(shell): WorkspaceSidebar (favorites/pinned/collections/recents) + SidebarNodeLink; drag-source deferred"
```

---

### Task 17: Swap DefaultLayout to WorkspaceShell; retire NavMenu + MainLayout

*Stream: WORKSPACE-SHELL · orderHint 44 · Depends on: Task 14 (WorkspaceShell), Task 15 (GlobalRail/TopBar/Breadcrumbs), Task 16 (WorkspaceSidebar/SidebarNodeLink)*

> **Reconciliation:** `FocusOnNavigate Selector="h1"` requires every routed page to emit a non-editable `<h1>`; `NodeHeader` (Task 24) wraps `InlineTitleEditor` in a static `<h1>` to satisfy this on the Node page. Deleting `MainLayout` retires the `NookTheme` wiring added to it in Task 10 — the theme now flows through WorkspaceShell.

**Files:**
- Modify: `Components/Routes.razor:6` — `DefaultLayout="typeof(Layout.WorkspaceShell)"`
- Modify/Delete: `Components/Layout/MainLayout.razor` (delete, or reduce to a thin wrapper)
- Delete: `Components/Layout/NavMenu.razor` (its nav is superseded by GlobalRail + WorkspaceSidebar)
- Verify: `FocusOnNavigate Selector="h1"` still resolves on every routed page

**Interfaces:**
_Consumes_:
- `WorkspaceShell` as the app-wide `DefaultLayout` (from task 41)
- `FocusOnNavigate` in Routes.razor targets `h1` — every routed page MUST emit a non-editable `<h1>` (NodeHeader wraps InlineTitleEditor in a static `<h1>` in the NODES stream)

_Produces_:
- App renders inside WorkspaceShell for all `[Authorize]` routes; no more MudDrawer/NavMenu

**Steps:**

- [ ] **Step 1: Inventory current pages' `<h1>` usage** so FocusOnNavigate keeps working after the layout swap.
```bash
cd C:/Users/capnb/source/repos/Nook && git grep -l "@page" -- Components | wc -l
```
Then use Grep for `<h1` and `PageTitle`/`MudText Typo="Typo.h`" across `Components/Pages` to find pages that render their title via MudText instead of a real `<h1>`. Any page lacking an `<h1>` will break `FocusOnNavigate Selector="h1"`. List them; each needs at least one non-editable `<h1>` (can be visually-hidden). This is a verification/scan step, not a mass rewrite — flag offenders for their owning stream.

- [ ] **Step 2: Swap the DefaultLayout in `Components/Routes.razor`.**
```razor
<AuthorizeRouteView RouteData="routeData" DefaultLayout="typeof(Layout.WorkspaceShell)">
```
Leave `<FocusOnNavigate RouteData="routeData" Selector="h1" />` unchanged.

- [ ] **Step 3: Retire NavMenu + MainLayout.** Delete `Components/Layout/NavMenu.razor` and `Components/Layout/MainLayout.razor` (WorkspaceShell fully replaces both). Before deleting, confirm nothing else references them:
```bash
git grep -n "NavMenu\|MainLayout" -- Components
```
Expect: only the (now-updated) Routes.razor and the files themselves. If any component hard-references `MainLayout` via `@layout`, either remove that directive (so it inherits DefaultLayout) or repoint it to `WorkspaceShell`. If a full delete is risky, reduce MainLayout to `@inherits LayoutComponentBase` + `@Body` as a temporary shim and note it.
```bash
git rm Components/Layout/NavMenu.razor Components/Layout/MainLayout.razor
```

- [ ] **Step 4: Build.**
```bash
dotnet build Nook.sln
```
Expect: `Build succeeded. 0 Error(s)`. Fix any dangling `@layout MainLayout` references surfaced by the compiler.

- [ ] **Step 5: Manual verify each destination still routes.** `dotnet watch` → http://localhost:5176 → walk the former NavMenu destinations and confirm each renders inside WorkspaceShell (rail + sidebar + topbar visible, content in the grid's content cell) and that keyboard focus lands on the page `<h1>` after navigation (tab once, or inspect `document.activeElement`):
  - /today, /capture, /inbox, /all, /nookryptex
  - /notes, /people, /projects, /places, /collections
  - /actions, /events, /timeline, /analytics
  - /search, /unassigned, /archive, /log, /tags, /admin/graph-migration

For any page that does NOT focus an `<h1>` (from the Step 1 scan), record it as a follow-up for its owning stream — do not silently leave FocusOnNavigate broken. No bUnit test; this is manual route verification.

- [ ] **Step 6: Commit.**
```bash
git add Components/Routes.razor && git commit -m "feat(shell): swap DefaultLayout to WorkspaceShell; retire NavMenu + MainLayout"
```

---

### Task 18: Add Command record + pure CommandRegistry.Match with xUnit TDD

*Stream: COMMAND-PALETTE · orderHint 50 · Depends on: none*

> **Contract:** produces `record Command(string Group, string Label, string Icon, string? Shortcut, Func<Task> Invoke)` and pure `CommandRegistry.Match` — consumed by CommandPalette (Tasks 19, 20).

**Files:**
- Create: `Services/CommandRegistry.cs` — top-level `record Command(...)` + static `CommandRegistry` class with pure `Match`.
- Test: `Nook.Tests/CommandRegistryTests.cs` — real xUnit, pure (no DB/harness needed).
- Read first: `Nook.Tests/CryptexEngineTests.cs` (pure-logic test pattern to copy), `Services/NodeUi.cs` (icon constants for Command.Icon usage).

**Interfaces:**

Produces (build these EXACT signatures):
```csharp
namespace Nook.Services;
public record Command(string Group, string Label, string Icon, string? Shortcut, Func<Task> Invoke);
public static class CommandRegistry
{
    // PURE. Case-insensitive subsequence fuzzy match over Label.
    // Ranking: exact-prefix < contiguous-substring < subsequence; tie-break shorter Label, then original order.
    // Empty/whitespace query => returns `all` in original order (caller supplies recents/defaults ordering).
    public static IEnumerable<Command> Match(string query, IReadOnlyList<Command> all);
}
```

Consumes: nothing (pure). `Command.Icon` is a MudBlazor icon string; `Command.Group` is the palette section label ("Actions", "Go to", "Recents", "Nodes").

**Steps:**

- [ ] **Step 1: Read the pure-logic test pattern.** Read `Nook.Tests/CryptexEngineTests.cs` fully — copy its style: `using Nook.Services; using Xunit;`, `[Fact]` methods, static builder helper, `Assert.Equal`. No `GraphHarness`, no DB — `CommandRegistry.Match` is pure so tests construct `Command` records inline.

- [ ] **Step 2: Write the FAILING tests first (TDD).** Create `Nook.Tests/CommandRegistryTests.cs`:
```csharp
using Nook.Services;
using Xunit;

namespace Nook.Tests;

public class CommandRegistryTests
{
    private static Command C(string label, string group = "Actions") =>
        new(group, label, "", null, () => Task.CompletedTask);

    private static readonly IReadOnlyList<Command> All = new List<Command>
    {
        C("New Note"), C("New Project"), C("Toggle Dark Mode"),
        C("Go to Inbox"), C("Go to Search"),
    };

    [Fact]
    public void EmptyQuery_ReturnsAllInOriginalOrder()
    {
        var hits = CommandRegistry.Match("", All).ToList();
        Assert.Equal(All.Select(c => c.Label), hits.Select(c => c.Label));
    }

    [Fact]
    public void WhitespaceQuery_ReturnsAllInOriginalOrder()
    {
        var hits = CommandRegistry.Match("   ", All).ToList();
        Assert.Equal(5, hits.Count);
    }

    [Fact]
    public void Subsequence_MatchesOutOfOrderGapChars()
    {
        // 'tgl' is a subsequence of "Toggle Dark Mode" (T..g..l) but not of the others
        var hits = CommandRegistry.Match("tgl", All).Select(c => c.Label).ToList();
        Assert.Contains("Toggle Dark Mode", hits);
        Assert.DoesNotContain("New Note", hits);
    }

    [Fact]
    public void IsCaseInsensitive()
    {
        var hits = CommandRegistry.Match("NEW NOTE", All).Select(c => c.Label).ToList();
        Assert.Contains("New Note", hits);
    }

    [Fact]
    public void RanksExactPrefixFirst()
    {
        // "new" is a prefix of "New Note"/"New Project"; a subsequence-only hit must rank lower
        var all = new List<Command> { C("Rename Node"), C("New Note") };
        var hits = CommandRegistry.Match("new", all).Select(c => c.Label).ToList();
        Assert.Equal("New Note", hits[0]); // prefix beats subsequence-in-"reName"
    }

    [Fact]
    public void NoMatch_ReturnsEmpty()
    {
        Assert.Empty(CommandRegistry.Match("zzzz", All));
    }
}
```

- [ ] **Step 3: Run tests — expect RED (does not compile / fails).**
```
dotnet test Nook.sln --filter "FullyQualifiedName~CommandRegistryTests"
```
Expected: build error `CommandRegistry does not exist` (this is the failing-first state).

- [ ] **Step 4: Minimal implementation.** Create `Services/CommandRegistry.cs`:
```csharp
namespace Nook.Services;

public record Command(string Group, string Label, string Icon, string? Shortcut, Func<Task> Invoke);

public static class CommandRegistry
{
    public static IEnumerable<Command> Match(string query, IReadOnlyList<Command> all)
    {
        if (string.IsNullOrWhiteSpace(query))
            return all;
        var q = query.Trim();
        return all
            .Select((c, i) => (c, i, rank: Rank(q, c.Label)))
            .Where(x => x.rank >= 0)
            .OrderBy(x => x.rank)
            .ThenBy(x => x.c.Label.Length)
            .ThenBy(x => x.i)
            .Select(x => x.c)
            .ToList();
    }

    // -1 = no match; lower is better (0 prefix, 1 substring, 2 subsequence).
    private static int Rank(string q, string label)
    {
        var l = label ?? string.Empty;
        if (l.StartsWith(q, StringComparison.OrdinalIgnoreCase)) return 0;
        if (l.Contains(q, StringComparison.OrdinalIgnoreCase)) return 1;
        return IsSubsequence(q, l) ? 2 : -1;
    }

    private static bool IsSubsequence(string q, string l)
    {
        int qi = 0;
        for (int li = 0; li < l.Length && qi < q.Length; li++)
            if (char.ToLowerInvariant(l[li]) == char.ToLowerInvariant(q[qi])) qi++;
        return qi == q.Length;
    }
}
```

- [ ] **Step 5: Run tests — expect GREEN.**
```
dotnet test Nook.sln --filter "FullyQualifiedName~CommandRegistryTests"
```
Expected: `Passed!  - Failed: 0, Passed: 6`.

- [ ] **Step 6: Full build sanity.**
```
dotnet build Nook.sln
```
Expected: 0 errors.

- [ ] **Step 7: Commit.**
```
git add Services/CommandRegistry.cs Nook.Tests/CommandRegistryTests.cs
git commit -m "feat(palette): pure CommandRegistry.Match with subsequence fuzzy + xUnit"
```

---

### Task 19: Build CommandPalette.razor overlay with keyboard nav and debounced server search

*Stream: COMMAND-PALETTE · orderHint 51 · Depends on: Task 18 (CommandRegistry), Task 14 (WorkspaceShell), Task 13 (WorkspaceState)*

> **Reconciliation:** `Command`/`CommandRegistry.Match` match Task 18; `NodeFilter.Take` is Task 2; `WorkspaceState.Recents` is Task 13. This task FINALIZES the shell↔palette wiring the Task 14 placeholder left open: shell holds `_paletteOpen`/`_commands`, renders `<CommandPalette Open OpenChanged Actions @ref>`, and exposes `[JSInvokable] OpenPalette`; the palette's imperative open is `Show()`. EXTEND the existing `WorkspaceShell.razor.js` `initialize` (its mod+k routing already exists from Task 14) — do NOT add a second module import. `n.Id` reads `Node.NodeId`.

**Files:**
- Create: `Components/Workspace/CommandPalette.razor` — the ⌘K overlay.
- Modify: `Components/Layout/WorkspaceShell.razor` — render `<CommandPalette @ref/@bind-Open>` at shell root; hold `_paletteOpen`.
- Modify: `Components/Layout/WorkspaceShell.razor.js` — add ⌘K / Ctrl+K keydown listener that invokes the shell DotNetRef to open the palette (collocated JS per contract).
- Modify: `wwwroot/css/nook-tokens.css` — palette overlay/panel styles (uses kind-accent + sand/dark tokens).
- Read first: `Services/CommandRegistry.cs` (from task 50), `Services/INodeService.cs` + `Services/NodeFilter.cs` (SearchText + new `Take`), `Services/NodeUi.cs` (Icon/KindAccent), `Components/Pages/SearchPage.razor` (existing debounce pattern to mirror at 250ms).

**Interfaces:**

Consumes:
```csharp
CommandRegistry.Match(string, IReadOnlyList<Command>)   // task 50
INodeService.QueryAsync(new NodeFilter { SearchText = q, Take = 8 })  // Take added by BACKEND stream
WorkspaceState.Recents  // IReadOnlyList<RecentNode>, from SHELL stream
NodeUi.Icon(NodeKind), NodeUi.KindAccentVar(NodeKind)
```

Produces (SHELL stream consumes these):
```razor
@* CommandPalette.razor *@
[Parameter] public bool Open { get; set; }
[Parameter] public EventCallback<bool> OpenChanged { get; set; }
[Parameter] public IReadOnlyList<Command> Actions { get; set; } = Array.Empty<Command>();  @* wired in task 52 *@
public void Show();   @* imperative open, callable via @ref from shell *@
```
JS (WorkspaceShell.razor.js) exports/behavior: a keydown handler registered in the shell module's `initialize(dotNetRef)` that, on (metaKey||ctrlKey)&&key==='k', preventDefault + `dotNetRef.invokeMethodAsync('OpenPalette')`. Shell exposes `[JSInvokable] Task OpenPalette()`.

NOTE: no bUnit — this task is BUILD + MANUAL verify only.

**Steps:**

- [ ] **Step 1: Confirm dependencies exist.** Read `Services/CommandRegistry.cs`, `Services/NodeFilter.cs` (must have `int? Take`), and `Services/WorkspaceState.cs` (must expose `Recents`). If `NodeFilter.Take` or `WorkspaceState` are missing, STOP — they belong to the BACKEND/SHELL streams; this task depends on them. (Coordinate; do not duplicate.)

- [ ] **Step 2: Build `Components/Workspace/CommandPalette.razor` — markup + state.** A fixed-position overlay (hidden when `!Open`) containing a search input and a scrollable results list grouped into sections Actions / Nodes / Recents / Go to. Skeleton:
```razor
@using Nook.Models
@using Nook.Services
@inject INodeService NodeService
@inject WorkspaceState Workspace
@inject NavigationManager Nav

@if (Open)
{
<div class="nook-palette-overlay" @onclick="CloseAsync">
  <div class="nook-palette" @onclick:stopPropagation="true" @onkeydown="OnKeyDown">
    <input @ref="_input" class="nook-palette-input" placeholder="Type a command or search…"
           @bind="_query" @bind:event="oninput" @oninput="OnQueryChanged" />
    <div class="nook-palette-list">
      @{ var i = 0; }
      @foreach (var group in _visible.GroupBy(r => r.Command.Group))
      {
        <div class="nook-palette-section">@group.Key</div>
        @foreach (var row in group)
        {
          var idx = i++;
          <div class="nook-palette-row @(idx == _active ? "is-active" : "")"
               @onclick="() => InvokeAsync(row)" @onmouseenter="() => _active = idx">
            <MudIcon Icon="@row.Command.Icon" Size="Size.Small" />
            <span class="nook-palette-label">@row.Command.Label</span>
            @if (row.Command.Shortcut is not null) { <kbd>@row.Command.Shortcut</kbd> }
          </div>
        }
      }
      @if (_visible.Count == 0) { <div class="nook-palette-empty">No results</div> }
    </div>
  </div>
</div>
}
```

- [ ] **Step 3: Implement the @code block — merge Actions + server Nodes + Recents, instant client filter, debounced (250ms) server search.**
```csharp
@code {
    [Parameter] public bool Open { get; set; }
    [Parameter] public EventCallback<bool> OpenChanged { get; set; }
    [Parameter] public IReadOnlyList<Command> Actions { get; set; } = Array.Empty<Command>();

    private ElementReference _input;
    private string _query = "";
    private int _active;
    private CancellationTokenSource? _debounce;
    private sealed record Row(Command Command);
    private List<Row> _nodeRows = new();
    private List<Row> _visible = new();

    public void Show() { Open = true; Recompute(); StateHasChanged(); }

    protected override void OnParametersSet() => Recompute();

    private void Recompute()
    {
        // Actions + Go-to filtered purely; Recents when query empty; Nodes from last server fetch.
        var actionHits = CommandRegistry.Match(_query, Actions).Select(c => new Row(c));
        IEnumerable<Row> recents = string.IsNullOrWhiteSpace(_query)
            ? Workspace.Recents.Select(r => new Row(new Command("Recents", r.Title, NodeUi.Icon(r.Kind), null,
                  () => { Nav.NavigateTo($"/nodes/{r.Id}"); return CloseAsync(); })))
            : Array.Empty<Row>();
        _visible = actionHits.Concat(_nodeRows).Concat(recents).ToList();
        if (_active >= _visible.Count) _active = Math.Max(0, _visible.Count - 1);
    }

    private async Task OnQueryChanged(ChangeEventArgs e)
    {
        _query = e.Value?.ToString() ?? "";
        _active = 0;
        Recompute();               // instant client-side (Actions/Recents)
        _debounce?.Cancel();
        _debounce = new CancellationTokenSource();
        var token = _debounce.Token;
        try { await Task.Delay(250, token); } catch (TaskCanceledException) { return; }
        if (string.IsNullOrWhiteSpace(_query)) { _nodeRows = new(); Recompute(); StateHasChanged(); return; }
        var nodes = await NodeService.QueryAsync(new NodeFilter { SearchText = _query, Take = 8 });
        if (token.IsCancellationRequested) return;
        _nodeRows = nodes.Select(n => new Row(new Command("Nodes", n.Title, NodeUi.Icon(n.Kind), null,
            () => { Nav.NavigateTo($"/nodes/{n.Id}"); return CloseAsync(); }))).ToList();
        Recompute();
        StateHasChanged();
    }

    private async Task InvokeAsync(Row row) { await row.Command.Invoke(); }

    private async Task OnKeyDown(KeyboardEventArgs e)
    {
        switch (e.Key)
        {
            case "ArrowDown": case "j" when e.CtrlKey: _active = Math.Min(_active + 1, _visible.Count - 1); break;
            case "ArrowUp":   case "k" when e.CtrlKey: _active = Math.Max(_active - 1, 0); break;
            case "Enter":
                if (_active < _visible.Count) await InvokeAsync(_visible[_active]);
                break;
            case "Escape": await CloseAsync(); break;
        }
    }

    private async Task CloseAsync()
    {
        Open = false; _query = ""; _nodeRows = new(); _active = 0;
        await OpenChanged.InvokeAsync(false);
    }

    protected override async Task OnAfterRenderAsync(bool first)
    {
        if (Open) { try { await _input.FocusAsync(); } catch { } }
    }
}
```
Note for the implementer: `⌘↵ create` and the toggle-theme/quick-capture Actions are wired in task 52 (they arrive via the `Actions` parameter + a create handler). Keep this task's Enter = open selected row.

- [ ] **Step 4: Add overlay CSS to `wwwroot/css/nook-tokens.css`.** Use existing tokens (`--canvas`, `--card`, `--ink`, `--line`). `.nook-palette-overlay` = fixed inset 0, high z-index, backdrop; `.nook-palette` = centered card max-width 640px, top ~15vh; `.nook-palette-row.is-active` uses an accent background; `.nook-palette-section` = small uppercase caption. Keep it token-driven so dark mode follows `data-theme`.

- [ ] **Step 5: Wire into WorkspaceShell.** In `Components/Layout/WorkspaceShell.razor` add near the shell root:
```razor
<CommandPalette @ref="_palette" Open="_paletteOpen" OpenChanged="v => _paletteOpen = v" Actions="_commands" />
```
and in `@code`: `private CommandPalette? _palette; private bool _paletteOpen; private IReadOnlyList<Command> _commands = Array.Empty<Command>();` plus `[JSInvokable] public Task OpenPalette() { _paletteOpen = true; StateHasChanged(); return Task.CompletedTask; }`. (`_commands` is populated in task 52.) **(Reconciliation: this replaces the Task 14 placeholder `<CommandPalette @ref="_palette" />` + private `OpenPalette()`→`_palette?.Open()`.)**

- [ ] **Step 6: ⌘K listener in `Components/Layout/WorkspaceShell.razor.js`.** In the shell module `initialize(dotNetRef)`, register:
```js
const onKey = (e) => {
  if ((e.metaKey || e.ctrlKey) && (e.key === 'k' || e.key === 'K')) {
    e.preventDefault();
    dotNetRef.invokeMethodAsync('OpenPalette');
  }
};
document.addEventListener('keydown', onKey);
// return/store a dispose that removeEventListener('keydown', onKey)
```
Ensure the shell passes `DotNetObjectReference.Create(this)` when importing the module in `OnAfterRenderAsync(firstRender)` and disposes it. (If the shell module already has an initialize, extend it — do not add a second import.)

- [ ] **Step 7: Build.**
```
dotnet build Nook.sln
```
Expected: 0 errors.

- [ ] **Step 8: MANUAL verify (no bUnit — observe in the running app).**
```
dotnet watch
```
Open http://localhost:5176, sign in, then:
  1. Press ⌘K (or Ctrl+K) → palette opens, input focused.
  2. Type `note` → Actions section filters instantly; after ~250ms a Nodes section lists matching nodes (verify one titled with your query).
  3. Press ArrowDown/ArrowUp → active row highlight moves; Enter on a Node row navigates to `/nodes/{id}` and closes the palette.
  4. Reopen, press Esc → closes. Empty query shows Recents section (after you've visited a node).
Confirm each observable outcome before proceeding.

- [ ] **Step 9: Commit.**
```
git add Components/Workspace/CommandPalette.razor Components/Layout/WorkspaceShell.razor Components/Layout/WorkspaceShell.razor.js wwwroot/css/nook-tokens.css
git commit -m "feat(palette): CommandPalette overlay with keyboard nav + debounced server search"
```

---

### Task 20: Wire palette Actions to real services (capture, create-kind, theme, go-to, create-on-cmd-enter)

*Stream: COMMAND-PALETTE · orderHint 52 · Depends on: Task 19 (CommandPalette overlay), Task 13 (ThemeState + WorkspaceState)*

> **Reconciliation:** `ThemeState`/`WorkspaceState` are the Task 13 services; `Command` is Task 18's record. `node.Id`/`n.Id` read `Node.NodeId`. Adds the `⌘↵` `OnCreate` param to the CommandPalette produced in Task 19.

**Files:**
- Modify: `Components/Layout/WorkspaceShell.razor` — build the `_commands` list from real services; pass to `<CommandPalette Actions="_commands">`.
- Modify: `Components/Workspace/CommandPalette.razor` — add `⌘↵` = create-from-query handler (`OnCreate` callback param).
- Read first: `Services/INodeService.cs` (`QuickCaptureAsync`, `CreateAsync`), `Services/NodeUi.cs` (`AssignableKinds`, `Icon`), `Services/ThemeState.cs` (SHELL stream — `SetAsync`/`IsDarkMode`), `Services/WorkspaceState.cs` (`Recents`), `Models/Node.cs` (Kind/Title/State fields for CreateAsync).

**Interfaces:**

Consumes:
```csharp
INodeService.QuickCaptureAsync(string title)                 // Inbox quick capture
INodeService.CreateAsync(new Node { Title = q, Kind = kind }) // per-kind create
NodeUi.AssignableKinds  // NodeKind[] to generate 'New {Kind}' actions
ThemeState.IsDarkMode / ThemeState.SetAsync(bool)            // toggle theme
NavigationManager.NavigateTo(string)                          // go-to destinations + /search deep-link
WorkspaceState.Recents                                        // already consumed in task 51
```

Produces:
```razor
@* CommandPalette.razor additive param *@
[Parameter] public EventCallback<string> OnCreate { get; set; }  // fired on ⌘↵ with current _query
```
`_commands` (in WorkspaceShell) is the `IReadOnlyList<Command>` already consumed by CommandPalette.Actions in task 51 — this task fills it.

**Steps:**

- [ ] **Step 1: Confirm ThemeState + Node shape.** Read `Services/ThemeState.cs` (need `IsDarkMode` + `Task SetAsync(bool)`), and `Models/Node.cs` to confirm `CreateAsync` accepts a `Node { Title, Kind }`. If `ThemeState` is absent it belongs to the SHELL stream — this task depends on it.

- [ ] **Step 2: Add the `⌘↵` create hook to CommandPalette.** In `Components/Workspace/CommandPalette.razor`:
  - Add `[Parameter] public EventCallback<string> OnCreate { get; set; }`.
  - In `OnKeyDown`, before the `Enter` case, intercept: `case "Enter" when (e.MetaKey || e.CtrlKey): if (!string.IsNullOrWhiteSpace(_query)) { await OnCreate.InvokeAsync(_query.Trim()); await CloseAsync(); } break;`.
  - Pass `OnCreate` through from the shell.

- [ ] **Step 3: Build `_commands` in WorkspaceShell from real services.** Inject `INodeService NodeService`, `ThemeState Theme`, `NavigationManager Nav`, `WorkspaceState Workspace` into `Components/Layout/WorkspaceShell.razor`, then populate `_commands` in `OnInitialized`:
```csharp
private void BuildCommands()
{
    var list = new List<Command>
    {
        new("Actions", "Quick capture to Inbox", Icons.Material.Filled.FlashOn, "⌘↵",
            async () => { var n = await NodeService.QuickCaptureAsync("Untitled"); await Workspace.NoteVisitedAsync(n.Id); Nav.NavigateTo($"/nodes/{n.Id}"); _paletteOpen = false; }),
        new("Actions", Theme.IsDarkMode ? "Switch to light mode" : "Switch to dark mode",
            Icons.Material.Filled.DarkMode, null,
            async () => { await Theme.SetAsync(!Theme.IsDarkMode); BuildCommands(); }),
    };
    foreach (var kind in NodeUi.AssignableKinds)
    {
        var k = kind;
        list.Add(new("Actions", $"New {k}", NodeUi.Icon(k), null,
            async () => {
                var node = await NodeService.CreateAsync(new Node { Title = $"New {k}", Kind = k });
                await Workspace.NoteVisitedAsync(node.Id);
                Nav.NavigateTo($"/nodes/{node.Id}"); _paletteOpen = false;
            }));
    }
    // Go-to destinations
    void Go(string label, string icon, string url) =>
        list.Add(new("Go to", label, icon, null, () => { Nav.NavigateTo(url); _paletteOpen = false; return Task.CompletedTask; }));
    Go("Go to Inbox", Icons.Material.Filled.Inbox, "/inbox");
    Go("Go to Search", Icons.Material.Filled.Search, "/search");
    Go("Go to Home", Icons.Material.Filled.Home, "/");
    _commands = list;
}
```
Verify the go-to URLs against the real routes (`@page` directives) before finalizing; adjust labels/paths to whatever exists.

- [ ] **Step 4: Wire ⌘↵ create at the shell.** Pass `OnCreate="HandleCreate"` to `<CommandPalette>`, where:
```csharp
private async Task HandleCreate(string query)
{
    var n = await NodeService.QuickCaptureAsync(query);
    await Workspace.NoteVisitedAsync(n.Id);
    Nav.NavigateTo($"/nodes/{n.Id}");
    _paletteOpen = false;
}
```

- [ ] **Step 5: Build.**
```
dotnet build Nook.sln
```
Expected: 0 errors. (If `CreateAsync` requires more required fields on `Node`, set them here — check `Models/Node.cs`.)

- [ ] **Step 6: MANUAL verify each action end-to-end.**
```
dotnet watch
```
At http://localhost:5176, open the palette (⌘K) and verify:
  1. `Quick capture to Inbox` → creates a node, navigates to `/nodes/{id}`, node appears in Recents next open.
  2. `New Note` / `New Project` etc. → creates a node of that Kind (confirm kind on the node page).
  3. `Switch to dark/light mode` → `data-theme` on `<html>` flips, UI recolors, label toggles.
  4. `Go to Search` / `Go to Inbox` → navigates to the right route.
  5. Type free text, press ⌘↵ → quick-captures a node titled with your text and opens it.
Confirm each before committing.

- [ ] **Step 7: Commit.**
```
git add Components/Layout/WorkspaceShell.razor Components/Workspace/CommandPalette.razor
git commit -m "feat(palette): wire Actions to capture/create/theme/go-to + cmd-enter create"
```

---

### Task 21: Retire app-bar search + OnSearchKeyUp; keep /search as deep-link results page

*Stream: COMMAND-PALETTE · orderHint 53 · Depends on: Task 20 (wired palette Actions), Task 15 (TopBar)*

> **Reconciliation:** TopBar (Task 15) now owns the search affordance (palette trigger), so removing MainLayout's search box does not orphan search. If Task 17 already deleted/replaced `MainLayout.razor`, treat this as a verify that no `OnSearchKeyUp`/`_search` remnants survive and that `/search` still resolves.

**Files:**
- Modify: `Components/Layout/MainLayout.razor` — remove the `MudTextField` search box + `OnSearchKeyUp` + `_search` field (superseded by TopBar/palette). If the app has fully migrated to `WorkspaceShell`, delete/retire MainLayout's app-bar search accordingly; do NOT remove `/search`.
- Keep untouched: `Components/Pages/SearchPage.razor` (`@page "/search"`) — remains the deep-link results page; palette 'Go to Search' + `?q=` links still target it.
- Read first: `Components/Layout/MainLayout.razor`, `Components/Workspace/TopBar.razor` (SHELL stream — confirm search entry now lives there / opens palette), `Components/Pages/SearchPage.razor`.

**Interfaces:**

Consumes: `TopBar.razor` (SHELL stream) — the app-bar search affordance is replaced by TopBar's palette-trigger, so removing MainLayout's box does not orphan search.

Produces: no new API. Invariant preserved: route `/search` still resolves to `SearchPage.razor`, which reads `?q=` and calls `INodeService.QueryAsync(new NodeFilter { SearchText = q })`. Palette 'Go to Search' and any `Nav.NavigateTo($"/search?q=...")` deep links keep working.

**Steps:**

- [ ] **Step 1: Confirm the replacement exists.** Read `Components/Workspace/TopBar.razor` and confirm it provides the search entry point (a palette trigger / ⌘K hint). If TopBar is not yet present, STOP — it is a SHELL-stream dependency; retiring MainLayout's box first would orphan search.

- [ ] **Step 2: Remove the app-bar search from `Components/Layout/MainLayout.razor`.** Delete the `<MudTextField ... OnKeyUp="OnSearchKeyUp" />` element (lines ~14-18), the `OnSearchKeyUp` method, and the `private string? _search;` field. Leave the menu button, title, `+` capture button, and theme toggle intact (those are handled by the shell migration streams; only the search box is this task's scope). **(Reconciliation: if Task 17 already removed `MainLayout.razor`, this reduces to the Step 3 verification.)**

- [ ] **Step 3: Verify no dangling references.**
```
grep -rn "OnSearchKeyUp\|_search" Components/
```
Expected: no matches (or only unrelated ones). Fix any leftovers.

- [ ] **Step 4: Confirm /search still exists and is reachable.** Ensure `Components/Pages/SearchPage.razor` still has `@page "/search"` and reads `[SupplyParameterFromQuery(Name = "q")]`. Do not modify it.

- [ ] **Step 5: Build.**
```
dotnet build Nook.sln
```
Expected: 0 errors, no warnings about the removed members.

- [ ] **Step 6: MANUAL verify.**
```
dotnet watch
```
At http://localhost:5176:
  1. The old app-bar search box is gone.
  2. Navigate directly to `/search?q=note` → SearchPage renders results for `note` (deep-link still works).
  3. Palette 'Go to Search' → lands on `/search`.
Confirm all three.

- [ ] **Step 7: Commit.**
```
git add Components/Layout/MainLayout.razor
git commit -m "refactor(palette): retire app-bar search; /search stays as deep-link results page"
```

---

### Task 22: Build and vendor the TipTap editor bundle via pinned esbuild, wired through the ImportMap

*Stream: EDITOR-INTEROP · orderHint 61 · Depends on: Task 1 (interop spike)*

> **Contract:** produces the vendored `nook-editor` ESM bundle reached by EditorHost (Task 23) via the bare specifier `"nook-editor"`. This is the ONLY sanctioned npm/build step (isolated in `/editor-src`); the app build needs no Node.

**Files:**
- Create `editor-src/.nvmrc` (Node 20 LTS pin)
- Create `editor-src/package.json` (pinned esbuild + TipTap deps, `build` script)
- Create `editor-src/index.mjs` (bundle entry — re-exports the TipTap pieces EditorHost mounts)
- Create `editor-src/build.mjs` (esbuild config: ESM, minified, content-hashed output + metafile)
- Create `editor-src/.gitignore` (`node_modules/`)
- Produce (checked in) `wwwroot/lib/editor/nook-editor.<hash>.js` — the vendored bundle; committed so the app build needs NO Node
- Modify `Components/App.razor:15` — replace bare `<ImportMap />` with one carrying an `nook-editor` specifier pointing at the hashed file
- Test: none. The bundle is developer-tooling output; verification is `dotnet build` (app builds with no Node) + confirming the file exists and the ImportMap resolves. No unit test.

Note: `wwwroot/lib/` exists (currently only `bootstrap/`); create the `editor/` subfolder. `editor-src/` does not exist yet.

**Interfaces:**
- Consumes: npm packages `@tiptap/core`, `@tiptap/starter-kit`, `@tiptap/extension-task-list`, `@tiptap/extension-task-item`, `tiptap-markdown` (developer machine only, at bundle time).
- Produces: `wwwroot/lib/editor/nook-editor.<hash>.js` — an ESM module exporting `{ Editor, StarterKit, TaskList, TaskItem, Markdown }`. Reached by EditorHost (Task 3) via the bare specifier `"nook-editor"`, mapped in the ImportMap. The hash is esbuild's content hash; only the ONE ImportMap line in App.razor changes when the bundle is rebuilt (EditorHost imports the stable specifier, never the hash) — that indirection is the entire point of routing through the ImportMap.

**Steps:**

- [ ] **Step 1: Pin Node.** Write `editor-src/.nvmrc`:

```
20
```

- [ ] **Step 2: package.json with pinned deps.** Write `editor-src/package.json`:

```json
{
  "name": "nook-editor-src",
  "version": "1.0.0",
  "private": true,
  "type": "module",
  "scripts": {
    "build": "node build.mjs"
  },
  "devDependencies": {
    "esbuild": "0.24.0"
  },
  "dependencies": {
    "@tiptap/core": "2.10.3",
    "@tiptap/starter-kit": "2.10.3",
    "@tiptap/extension-task-list": "2.10.3",
    "@tiptap/extension-task-item": "2.10.3",
    "tiptap-markdown": "0.8.10"
  }
}
```

(Versions are pins; the developer may bump to the latest matching set at build time, but they MUST be exact pins, not ranges.)

- [ ] **Step 3: Bundle entry.** Write `editor-src/index.mjs` — re-export exactly what EditorHost mounts, keeping the .razor.js thin:

```js
export { Editor } from "@tiptap/core";
export { default as StarterKit } from "@tiptap/starter-kit";
export { TaskList } from "@tiptap/extension-task-list";
export { TaskItem } from "@tiptap/extension-task-item";
export { Markdown } from "tiptap-markdown";
```

- [ ] **Step 4: esbuild config with content hash + metafile.** Write `editor-src/build.mjs`:

```js
import { build } from "esbuild";
import { rm, readdir, writeFile } from "node:fs/promises";

const outdir = "../wwwroot/lib/editor";

// clean previous hashed outputs so stale bundles never linger
try {
  for (const f of await readdir(outdir)) {
    if (f.startsWith("nook-editor.") && f.endsWith(".js")) await rm(`${outdir}/${f}`);
  }
} catch { /* dir may not exist yet */ }

const result = await build({
  entryPoints: ["index.mjs"],
  bundle: true,
  format: "esm",
  minify: true,
  sourcemap: false,
  target: ["es2020"],
  outdir,
  entryNames: "nook-editor.[hash]",
  metafile: true,
});

const outFile = Object.keys(result.metafile.outputs)
  .map((p) => p.split("/").pop())
  .find((n) => n.startsWith("nook-editor.") && n.endsWith(".js"));

await writeFile("build-manifest.json", JSON.stringify({ outFile }, null, 2));
console.log("\n==> Bundle written: wwwroot/lib/editor/" + outFile);
console.log("==> Paste this filename into the ImportMap 'nook-editor' entry in Components/App.razor\n");
```

- [ ] **Step 5: gitignore node_modules.** Write `editor-src/.gitignore`:

```
node_modules/
```

- [ ] **Step 6: Build the bundle (developer machine, one-time; needs Node 20).** Run:

```
cd editor-src && npm install && npm run build
```

Expected: esbuild finishes and prints `==> Bundle written: wwwroot/lib/editor/nook-editor.<hash>.js` plus the paste reminder. Note the exact `<hash>` filename from the output (also stored in `editor-src/build-manifest.json`).

- [ ] **Step 7: Wire the ImportMap.** Edit `Components/App.razor`. Add `@using Microsoft.AspNetCore.Components` at the top if absent, replace line 15's `<ImportMap />` with `<ImportMap AdditionalImportMapDefinition="EditorImportMap" />`, and add to the `@code` block (substitute the real hashed filename from Step 6):

```csharp
private static readonly ImportMapDefinition EditorImportMap = new(
    new Dictionary<string, string>
    {
        ["nook-editor"] = "/lib/editor/nook-editor.<hash>.js"
    },
    scopes: null,
    integrity: null);
```

This lets EditorHost do `import * as NookEditor from "nook-editor"` regardless of the hash.

- [ ] **Step 8: Verify the app builds with NO Node and the asset is importable.** From the repo root run:

```
dotnet build Nook.sln
```

Expected: `Build succeeded. 0 Error(s)`. Then `dotnet watch`, open `http://localhost:5176`, open devtools console and run `await import("/lib/editor/nook-editor.<hash>.js")` — expect a module object exposing `Editor`, `StarterKit`, `TaskList`, `TaskItem`, `Markdown`. Confirm the committed bundle (not node_modules) is what gets served.

- [ ] **Step 9: Commit the source AND the vendored output.** Run:

```
git add editor-src wwwroot/lib/editor Components/App.razor
git commit -m "feat(editor): vendor TipTap bundle via pinned esbuild + ImportMap 'nook-editor'"
```

(Include the required `Co-Authored-By` trailer. `node_modules/` is git-ignored; the hashed `.js` bundle IS committed so CI/app build never needs Node.)

---

### Task 23: Implement EditorHost (component + JS module) to the EDITOR JS CONTRACT

*Stream: EDITOR-INTEROP · orderHint 62 · Depends on: Task 1 (interop spike), Task 22 (TipTap bundle) — plus backend Tasks 2/3/6*

> **Reconciliation:** consumes `INodeService.SaveBodyAsync` (Task 3), `NodeFilter.Take` (Task 2), `IWikiLinkService.ReconcileAsync`/`ResolveOrCreateAsync` (Task 6), and `import * as NookEditor from "nook-editor"` (Task 22) — all signatures match. The body parameter is `InitialBody` (this is the name NodePage/Task 27 must bind, reconciled there). If a backend method is unmerged, stub the JSInvokable body with a TODO so it still compiles.

**Files:**
- Create `Components/Nodes/EditorHost.razor` (InteractiveServer, IAsyncDisposable, `ShouldRender()=>false` after init)
- Create `Components/Nodes/EditorHost.razor.js` (client-owned editor state; `import * as NookEditor from "nook-editor"`)
- Delete `Components/Nodes/InteropSpike.razor` + `Components/Nodes/InteropSpike.razor.js` (spike retired now that the real host proves the pattern)
- Test: none — no bUnit / component harness in the spine. Verification is `dotnet build` + a manual `dotnet watch` typing/round-trip/reload observation. Do NOT invent a component unit test; the debounce + save round-trip can only be honestly checked by watching SignalR traffic.

CROSS-STREAM DEPENDENCIES (must exist before Step 4 verify passes): `INodeService.SaveBodyAsync(int, string?, CancellationToken)`, `IWikiLinkService.ReconcileAsync(...)` + `ResolveOrCreateAsync(...)`, `NodeFilter.Take`. These are owned by the BACKEND stream. If they are not yet merged, stub the `[JSInvokable]` bodies to compile and mark the verify step blocked on that stream — do NOT re-implement them here.

**Interfaces:**
- Consumes: `import * as NookEditor from "nook-editor"` (Task 2 bundle: `Editor`, `StarterKit`, `TaskList`, `TaskItem`, `Markdown`); `INodeService.SaveBodyAsync(int nodeId, string? body, CancellationToken)` and `INodeService.QueryAsync(NodeFilter{ SearchText, Take })`; `IWikiLinkService.ReconcileAsync(int sourceNodeId, IReadOnlyCollection<string> linkedTitles, CancellationToken)` and `ResolveOrCreateAsync(string title, CancellationToken)`; `IActionService.CreateAsync(ActionItem, contexts=null)`, `CompleteAsync(int)`, `ReopenAsync(int)`.
- Produces: `Components/Nodes/EditorHost.razor` with `[Parameter] int NodeId` (required) and `[Parameter] string? InitialBody`. `EditorHost.razor.js` exports `initialize(el, dotNetRef, initialMarkdown, opts)`, `flush()`, `dispose()` with `opts = { debounceMs: 800 }`. `[JSInvokable]` surface on the component (called from JS): `Task SaveBodyAsync(string markdown)`, `Task<string> SearchNodesAsync(string query)`, `Task<string> ResolveOrCreateLinkAsync(string title)`, `Task ToggleActionAsync(int id, bool done)`, `Task<int> CreateActionAsync(string text)`. NodePage.razor (WORKSPACE stream) renders `<EditorHost NodeId=... InitialBody=... />`.

**Steps:**

- [ ] **Step 1: Component shell + lifecycle (copies the proven spike pattern).** Write `Components/Nodes/EditorHost.razor`:

```razor
@rendermode InteractiveServer
@implements IAsyncDisposable
@using System.Text.Json
@using Microsoft.JSInterop
@using Nook.Models
@inject IJSRuntime JS
@inject INodeService Nodes
@inject IWikiLinkService WikiLinks
@inject IActionService Actions

<div @ref="_el" class="nook-editor"></div>

@code {
    [Parameter, EditorRequired] public int NodeId { get; set; }
    [Parameter] public string? InitialBody { get; set; }

    private ElementReference _el;
    private IJSObjectReference? _module;
    private DotNetObjectReference<EditorHost>? _ref;
    private bool _initialized;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender || _initialized) return;
        _initialized = true;
        _ref = DotNetObjectReference.Create(this);
        _module = await JS.InvokeAsync<IJSObjectReference>(
            "import", "./Components/Nodes/EditorHost.razor.js");
        await _module.InvokeVoidAsync("initialize", _el, _ref, InitialBody ?? "",
            new { debounceMs = 800 });
    }

    // Editor owns its DOM/state after init — never let Blazor re-render over it.
    protected override bool ShouldRender() => !_initialized;

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_module is not null)
            {
                await _module.InvokeVoidAsync("flush");   // persist any pending debounce
                await _module.InvokeVoidAsync("dispose");
                await _module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException) { }
        _ref?.Dispose();
    }
}
```

- [ ] **Step 2: The five `[JSInvokable]` methods.** Add to the `@code` block:

```csharp
[JSInvokable]
public async Task SaveBodyAsync(string markdown)
{
    await Nodes.SaveBodyAsync(NodeId, markdown);
    var titles = ExtractWikiTitles(markdown);
    await WikiLinks.ReconcileAsync(NodeId, titles);
}

[JSInvokable]
public async Task<string> SearchNodesAsync(string query)
{
    var results = await Nodes.QueryAsync(new NodeFilter { SearchText = query, Take = 8 });
    var dto = results.Select(n => new { id = n.Id, title = n.Title, kind = n.Kind.ToString() });
    return JsonSerializer.Serialize(dto);
}

[JSInvokable]
public async Task<string> ResolveOrCreateLinkAsync(string title)
{
    var (id, url) = await WikiLinks.ResolveOrCreateAsync(title);
    return JsonSerializer.Serialize(new { id, url });
}

[JSInvokable]
public Task ToggleActionAsync(int id, bool done) =>
    done ? Actions.CompleteAsync(id) : Actions.ReopenAsync(id);

[JSInvokable]
public async Task<int> CreateActionAsync(string text)
{
    var created = await Actions.CreateAsync(new ActionItem
    {
        Title = text,
        Kind = ActionKind.Task,
        TargetNodeId = NodeId
    });
    return created.Id;
}

private static IReadOnlyCollection<string> ExtractWikiTitles(string markdown)
{
    var titles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (System.Text.RegularExpressions.Match m in
        System.Text.RegularExpressions.Regex.Matches(markdown, @"\[\[([^\]]+)\]\]"))
        titles.Add(m.Groups[1].Value.Trim());
    return titles;
}
```

Before writing, open `Models/ActionItem.cs` and `Models/*Kind*.cs` to confirm the real property names (`Title`/`TargetNodeId`) and that `ActionKind.Task` exists; adjust to the actual members if they differ. If any cross-stream service method is not yet merged, keep the signature and put a `// TODO(blocked on BACKEND stream)` throwing body so the file still compiles.

- [ ] **Step 3: The JS module — client-owned state, 800ms debounce, [[title]] extraction.** Write `Components/Nodes/EditorHost.razor.js`:

```js
import * as NookEditor from "nook-editor";

let editor = null;
let dotNetRef = null;
let debounceMs = 800;
let timer = null;
let dirty = false;

export function initialize(el, ref, initialMarkdown, opts) {
    dotNetRef = ref;
    debounceMs = (opts && opts.debounceMs) || 800;

    editor = new NookEditor.Editor({
        element: el,
        extensions: [
            NookEditor.StarterKit,
            NookEditor.TaskList,
            NookEditor.TaskItem.configure({ nested: true }),
            NookEditor.Markdown, // serialize/deserialize markdown
        ],
        content: initialMarkdown, // parsed as markdown by the Markdown extension
        onUpdate: () => {
            dirty = true;
            if (timer) clearTimeout(timer);
            timer = setTimeout(save, debounceMs); // ONE round-trip per idle burst
        },
    });
}

function currentMarkdown() {
    return editor.storage.markdown.getMarkdown();
}

async function save() {
    if (timer) { clearTimeout(timer); timer = null; }
    if (!dirty || !dotNetRef) return;
    dirty = false;
    const md = currentMarkdown();
    await dotNetRef.invokeMethodAsync("SaveBodyAsync", md);
}

// Called from .NET DisposeAsync BEFORE dispose so a pending edit is not lost.
export async function flush() {
    await save();
}

export function dispose() {
    if (timer) clearTimeout(timer);
    timer = null;
    if (editor) { editor.destroy(); editor = null; }
    dotNetRef = null;
}
```

Note: [[title]] extraction for reconcile lives on the .NET side (`ExtractWikiTitles`, Step 2) so JS only ships the markdown. If the workspace stream wants a `[[ ]]` autocomplete popup, it will call `SearchNodesAsync`/`ResolveOrCreateLinkAsync` from a suggestion plugin later — the JSInvokable surface is already in place. Confirm the `tiptap-markdown` API (`editor.storage.markdown.getMarkdown()`) against the pinned version; adjust the accessor if the vendored version differs.

- [ ] **Step 4: Build.** Run:

```
dotnet build Nook.sln
```

Expected: `Build succeeded. 0 Error(s)`. (If blocked on unmerged backend signatures, the TODO-throwing bodies still compile — note the block.)

- [ ] **Step 5: Manual verification (honest — no automated test possible for this).** Run `dotnet watch`. Until NodePage (WORKSPACE stream) exists, verify via a scratch host: temporarily add `<EditorHost NodeId="1" InitialBody="@("# hi")" />` to a throwaway `@page "/editor-smoke"` (delete it before commit). Open the page with devtools Network/WS panel visible and confirm:
  1. The TipTap editor mounts and is typeable; console shows the `nook-editor` module loaded (no ImportMap resolution error).
  2. Type a burst of characters — NO per-keystroke SignalR frame. Exactly ONE `SaveBodyAsync` invocation fires ~800ms after you STOP typing (watch the WS frames).
  3. Include `[[Some Title]]` in the body, let it save, then check the DB / backlinks: a `mentions` relation to a node titled "Some Title" was created (proves `ExtractWikiTitles` + `ReconcileAsync`).
  4. Reload the page — the body persists (proves `SaveBodyAsync` wrote `Body`).
  5. Navigate away mid-edit (before the 800ms fires) — the pending edit is still saved (proves `flush()` on dispose) and NO `JSDisconnectedException` surfaces in the terminal.

- [ ] **Step 6: Retire the spike + commit.** Delete the spike files and any `/editor-smoke` scratch page, then commit:

```
git rm Components/Nodes/InteropSpike.razor Components/Nodes/InteropSpike.razor.js
git add Components/Nodes/EditorHost.razor Components/Nodes/EditorHost.razor.js
git commit -m "feat(editor): EditorHost host + TipTap module implementing the editor JS contract"
```

(Include the required `Co-Authored-By` trailer.)

---

### Task 24: Build InlineTitleEditor + NodeHeader (static h1 focus target) + ObjectTypeBadge

*Stream: Node Page Assembly · orderHint 70 · Depends on: Task 4 (NodeUi.KindAccent — also Task 9), Task 11 (ObjectTypeBadge)*

> **Reconciliation:** `Components/Shared/ObjectTypeBadge.razor` is already produced by Task 11 (VISUAL) — REUSE it; the Step 2 badge block here is a fallback only if Task 11 hasn't merged (do not create a second file). `NodeUi.KindAccent`/`KindAccentVar` come from Task 4 (identical to Task 9). Title rename uses `INodeService.UpdateAsync` (not `SaveBodyAsync`). Uses the `Node.NodeId` PK.

**Files:**
- Create: `Components/Shared/ObjectTypeBadge.razor` — kind pill: icon + label, tinted with `NodeUi.KindAccent(Kind)`.
- Create: `Components/Nodes/InlineTitleEditor.razor` — contenteditable title; persists via `INodeService`; hosts an inline SaveIndicator.
- Create: `Components/Nodes/NodeHeader.razor` — static `<h1 tabindex="-1">` focus target WRAPPING `InlineTitleEditor`, plus `ObjectTypeBadge` and an inline StateChip.
- Modify: none of the reused panels; these are all new leaf components.
- Test: NONE (Blazor components — no bUnit harness in the spine). Verification is `dotnet build` + `dotnet watch` manual observation (see steps).
- Read first: `Services/NodeUi.cs` (Icon/StateColor/StateLabel), `Services/INodeService.cs` (GetByIdAsync/UpdateAsync signatures), `Models/Node.cs` (Title/Kind/State/NodeTags/Tags), `Components/Pages/NodeDetail.razor` (existing header markup lines 26-32 for reference only).

**Interfaces:**
Consumes:
- `NodeUi.Icon(NodeKind) : string`, `NodeUi.StateColor(NodeState) : MudBlazor.Color`, `NodeUi.StateLabel(NodeState) : string` (existing, Services/NodeUi.cs).
- `NodeUi.KindAccent(NodeKind) : string` and `NodeUi.KindAccentVar(NodeKind) : string` (additive; produced by the Services/NodeUi stream — if not yet merged, add a temporary local `KindAccent` returning the spec hex and delete it once the shared method lands).
- `INodeService.GetByIdAsync(int) : Task<Node?>` and `INodeService.UpdateAsync(Node, IEnumerable<int>? tagIds = null) : Task` (existing). Title rename uses the UpdateAsync path — NOT SaveBodyAsync (SaveBodyAsync is body-only by contract).
Produces:
- `ObjectTypeBadge` params: `[Parameter] NodeKind Kind`, `[Parameter] bool Dense = false`.
- `InlineTitleEditor` params: `[Parameter] int NodeId`, `[Parameter] string Title`, `[Parameter] EventCallback<string> TitleChanged`.
- `NodeHeader` params: `[Parameter] Node Node` (required), `[Parameter] EventCallback<string> OnTitleChanged`.

**Steps:**

- [ ] **Step 1: Branch off main.** This stream's first task creates the shared feature branch; later Node-Page tasks continue on it.
```bash
git -C "C:/Users/capnb/source/repos/Nook" checkout main
git -C "C:/Users/capnb/source/repos/Nook" pull --ff-only
git -C "C:/Users/capnb/source/repos/Nook" checkout -b feature/node-page-assembly
```
Expected: `Switched to a new branch 'feature/node-page-assembly'`.

- [ ] **Step 2: Create `Components/Shared/ObjectTypeBadge.razor`.** A tiny presentational pill — no service injection. Tint from the kind accent so every kind reads distinctly. **(Reconciliation: SKIP if Task 11 already created this file; use its `Kind`/`ShowLabel`/`Size`/`Dense` contract instead.)**
```razor
@using Nook.Models
@using Nook.Services

<span class="otb" style="--otb-accent:@NodeUi.KindAccent(Kind);" title="@Kind">
    <MudIcon Icon="@NodeUi.Icon(Kind)" Size="@(Dense ? Size.Small : Size.Small)" />
    <span class="otb__label">@Kind</span>
</span>

@code {
    [Parameter] public NodeKind Kind { get; set; }
    [Parameter] public bool Dense { get; set; }
}
```
Add a collocated `ObjectTypeBadge.razor.css` giving `.otb{display:inline-flex;align-items:center;gap:.35rem;padding:.15rem .55rem;border-radius:999px;background:color-mix(in srgb,var(--otb-accent) 14%,transparent);color:var(--otb-accent);font:600 .8rem/1 var(--font-body,inherit);}` (scoped CSS is safe; no external fetch).

- [ ] **Step 3: Create `Components/Nodes/InlineTitleEditor.razor`.** Contenteditable div, commit on blur or Enter, persist via UpdateAsync (title path), show a transient SaveIndicator. Guard against empty titles (revert to previous). Do NOT bind two-way to a MudTextField — a raw contenteditable is what the h1 focus-target design needs.
```razor
@using Nook.Models
@using Nook.Services
@inject INodeService NodeService

<div class="ite">
    <div class="ite__field" contenteditable="true" role="textbox" aria-label="Node title"
         @ref="_el" @onblur="CommitAsync" @onkeydown="OnKeyDown">@_current</div>
    @if (_state == SaveState.Saving)
    {
        <span class="ite__save">Saving…</span>
    }
    else if (_state == SaveState.Saved)
    {
        <span class="ite__save ite__save--ok">Saved</span>
    }
</div>

@code {
    [Parameter] public int NodeId { get; set; }
    [Parameter] public string Title { get; set; } = "";
    [Parameter] public EventCallback<string> TitleChanged { get; set; }

    enum SaveState { Idle, Saving, Saved }
    private ElementReference _el;
    private string _current = "";
    private string _committed = "";
    private SaveState _state = SaveState.Idle;

    protected override void OnParametersSet()
    {
        // Re-sync only when the node identity/title changes from the outside,
        // never mid-edit (blur owns the commit).
        _current = Title; _committed = Title;
    }

    private async Task OnKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter") { await _el.FocusAsync(); await CommitAsync(new FocusEventArgs()); }
    }

    private async Task CommitAsync(FocusEventArgs _)
    {
        // Read the live text via a bound value fallback: use JS-free approach —
        // capture through @bind is not available on contenteditable, so read from
        // the element using a small JS interop OR keep _current updated via @oninput.
    }
}
```
IMPLEMENTATION NOTE for the executor: contenteditable has no native two-way bind. Add `@oninput="e => _current = e.Value?.ToString() ?? _current"` (Blazor surfaces innerText via `ChangeEventArgs.Value` for contenteditable with `@oninput`); if that proves unreliable in .NET 10, fall back to a 3-line collocated `InlineTitleEditor.razor.js` exporting `readText(el)` and call it in CommitAsync. In CommitAsync: trim `_current`; if empty → revert `_current=_committed` and return; if unchanged → return; else set `_state=Saving`, `StateHasChanged()`, then:
```csharp
var node = await NodeService.GetByIdAsync(NodeId);
if (node is not null)
{
    node.Title = _current.Trim();
    // preserve existing tags so UpdateAsync's tag-reconcile does not strip them
    await NodeService.UpdateAsync(node, node.Tags.Select(t => t.TagId).ToList());
    _committed = node.Title;
    await TitleChanged.InvokeAsync(_committed);
}
_state = SaveState.Saved; StateHasChanged();
```
VERIFY the executor confirms `UpdateAsync`'s null-vs-empty tagIds behavior by reading `Services/NodeService.cs UpdateAsync` before wiring — if passing an explicit tag-id list is destructive, pass `null` instead.

- [ ] **Step 4: Create `Components/Nodes/NodeHeader.razor`.** The `<h1>` is the STATIC focus target (so `<FocusOnNavigate Selector="h1" />` in Routes lands on a non-editable element, not inside the contenteditable). InlineTitleEditor is nested inside it.
```razor
@using Nook.Models
@using Nook.Services

<header class="node-header">
    <div class="node-header__meta">
        <ObjectTypeBadge Kind="Node.Kind" />
        <MudChip T="string" Size="Size.Small" Variant="Variant.Outlined"
                 Color="@NodeUi.StateColor(Node.State)">@NodeUi.StateLabel(Node.State)</MudChip>
    </div>
    <h1 class="node-header__title" tabindex="-1">
        <InlineTitleEditor NodeId="Node.NodeId" Title="@Node.Title" TitleChanged="OnTitleChanged" />
    </h1>
</header>

@code {
    [Parameter, EditorRequired] public Node Node { get; set; } = default!;
    [Parameter] public EventCallback<string> OnTitleChanged { get; set; }
}
```
Collocated `NodeHeader.razor.css`: `.node-header__title{font:600 clamp(1.6rem,3vw,2.2rem)/1.15 var(--font-display,inherit);margin:.25rem 0 0;outline:none;}` and reset the contenteditable so it inherits the h1 type (`.ite__field{outline:none;}` focus ring on `:focus-visible`). The StateChip is inline markup here (no separate file); SaveIndicator lives inside InlineTitleEditor.

- [ ] **Step 5: Build.**
```bash
dotnet build "C:/Users/capnb/source/repos/Nook/Nook.sln"
```
Expected: `Build succeeded. 0 Error(s)`. (If `NodeUi.KindAccent` is unresolved because the Services/NodeUi stream has not merged, add the temporary local helper noted in Interfaces, rebuild, and leave a `// TODO remove when NodeUi.KindAccent lands` marker.)

- [ ] **Step 6: Manual verification.** There is no component unit-test harness; observe behavior in the running app. Temporarily drop `<NodeHeader Node="someNode" />` onto an existing page (e.g. a scratch spot in `NodeDetail.razor` header, reverted after) OR wait for Task 4 and verify there.
```bash
dotnet watch --project "C:/Users/capnb/source/repos/Nook/Nook.csproj"
```
Open http://localhost:5176, navigate to a node. Observe: (a) the kind badge shows the correct icon + accent color per the KIND ACCENT MAP; (b) clicking the title text enters edit mode; (c) typing a new title and pressing Tab/Enter shows "Saving…" then "Saved"; (d) refresh the page — the new title persists; (e) clearing the title and blurring reverts to the previous title (no empty save). Confirm keyboard focus after navigation lands on the h1 (title not in edit mode) — press Tab from address bar / use browser focus ring.

- [ ] **Step 7: Commit.**
```bash
git -C "C:/Users/capnb/source/repos/Nook" add -A
git -C "C:/Users/capnb/source/repos/Nook" commit -m "feat(node-page): InlineTitleEditor, NodeHeader static-h1 focus target, ObjectTypeBadge"
```

---

### Task 25: Build PropertiesPanel composing reused panels + kind/state/pin/fav controls

*Stream: Node Page Assembly · orderHint 72 · Depends on: Task 24 (InlineTitleEditor + NodeHeader + ObjectTypeBadge)*

> **Reconciliation:** composes existing panels (ConnectionsPanel/ActionsPanel/CollectionAssignmentPanel/TagAutocomplete/TagChips) with ONLY their documented params; the empty `data-ai-slot` sections are reserved-by-design, not placeholders. Uses `Node.NodeId`.

**Files:**
- Create: `Components/Nodes/PropertiesPanel.razor` — collapsible right rail; composes REUSED panels unchanged and adds Kind/State/pin/fav controls plus empty reserved AI regions.
- Modify: NONE of `ConnectionsPanel.razor`, `ActionsPanel.razor`, `CollectionAssignmentPanel.razor`, `TagAutocomplete.razor`, `TagChips.razor` — they are reused as-is via their existing parameters.
- Test: NONE (Blazor component). Verify via build + dotnet watch.
- Read first: `Components/Shared/ConnectionsPanel.razor` (`NodeId`, `OnChanged`, public `OpenAdd()`), `Components/Shared/ActionsPanel.razor` (`NodeId`, `OnChanged`, public `OpenCreate()`), `Components/Shared/CollectionAssignmentPanel.razor` (`NodeId` int?, `ShowHeader`, `OnChanged`), `Components/Shared/TagAutocomplete.razor` (`AllTags`, `SelectedTagIds`, `SelectedTagIdsChanged`), `Components/Shared/TagChips.razor` (`Tags`), `Services/INodeService.cs` (PromoteAsync/SetStateAsync/TogglePinAsync/ToggleFavoriteAsync/ArchiveAsync/RestoreAsync), `Services/NodeUi.cs` (AssignableKinds, Icon).

**Interfaces:**
Consumes:
- `INodeService.PromoteAsync(int id, NodeKind kind) : Task`, `INodeService.SetStateAsync(int id, NodeState state) : Task`, `INodeService.TogglePinAsync(int id) : Task`, `INodeService.ToggleFavoriteAsync(int id) : Task`, `INodeService.ArchiveAsync(int id) : Task`, `INodeService.RestoreAsync(int id) : Task` (all existing, exact names).
- `ITagService.GetAllAsync() : Task<List<Tag>>` (existing — used to feed TagAutocomplete).
- Reused components' existing params (see Files/Read-first). Call them with ONLY their documented parameters; do not add params.
- `NodeUi.AssignableKinds`, `NodeUi.Icon`, `NodeUi.KindAccent`.
Produces:
- `PropertiesPanel` params: `[Parameter] Node Node` (required), `[Parameter] EventCallback OnChanged` (raised after any mutation so NodePage reloads), `[Parameter] bool Collapsed = false`, `[Parameter] EventCallback<bool> CollapsedChanged`.
- Reserved AI regions are empty `<section class="prop-ai" data-ai-slot="...">` blocks (no logic) so a later stream can fill them.

**Steps:**

- [ ] **Step 1: Confirm on the shared branch.**
```bash
git -C "C:/Users/capnb/source/repos/Nook" checkout feature/node-page-assembly
```
Expected: already on it (or switches to it).

- [ ] **Step 2: Create `Components/Nodes/PropertiesPanel.razor` shell + collapse.** Right-rail container with a header toggle bound to `Collapsed`/`CollapsedChanged`. Load tags once for the tag editor.
```razor
@using Nook.Models
@using Nook.Services
@inject INodeService NodeService
@inject ITagService TagService
@inject ISnackbar Snackbar

<aside class="prop-panel @(Collapsed ? "prop-panel--collapsed" : null)">
    <div class="prop-panel__head">
        <MudText Typo="Typo.subtitle2">Properties</MudText>
        <MudIconButton Size="Size.Small"
            Icon="@(Collapsed ? Icons.Material.Filled.ChevronLeft : Icons.Material.Filled.ChevronRight)"
            OnClick="ToggleCollapse" title="Collapse properties" />
    </div>
    @if (!Collapsed)
    {
        @* body sections in following steps *@
    }
</aside>

@code {
    [Parameter, EditorRequired] public Node Node { get; set; } = default!;
    [Parameter] public EventCallback OnChanged { get; set; }
    [Parameter] public bool Collapsed { get; set; }
    [Parameter] public EventCallback<bool> CollapsedChanged { get; set; }

    private List<Tag> _allTags = new();
    private List<int> _selectedTagIds = new();

    protected override async Task OnParametersSetAsync()
    {
        _allTags = await TagService.GetAllAsync();
        _selectedTagIds = Node.Tags.Select(t => t.TagId).ToList();
    }

    private async Task ToggleCollapse()
    {
        Collapsed = !Collapsed;
        await CollapsedChanged.InvokeAsync(Collapsed);
    }

    private async Task Notify() => await OnChanged.InvokeAsync();
}
```

- [ ] **Step 3: Add the Kind + State + pin/fav + archive control section.** Kind chips call `PromoteAsync`; a state select calls `SetStateAsync`; icon buttons call toggle/archive; each awaits then `Notify()` so NodePage reloads the node.
```razor
<section class="prop-section">
    <MudText Typo="Typo.overline">Kind</MudText>
    <div class="prop-kinds">
        @foreach (var k in NodeUi.AssignableKinds)
        {
            <MudChip T="string" Size="Size.Small" Icon="@NodeUi.Icon(k)"
                     Variant="@(k == Node.Kind ? Variant.Filled : Variant.Outlined)"
                     OnClick="@(() => SetKind(k))">@k</MudChip>
        }
    </div>
</section>
<section class="prop-section">
    <MudText Typo="Typo.overline">State</MudText>
    <MudSelect T="NodeState" Value="Node.State" ValueChanged="SetState" Dense="true">
        @foreach (var s in Enum.GetValues<NodeState>())
        { <MudSelectItem Value="s">@s</MudSelectItem> }
    </MudSelect>
    <div class="prop-flags">
        <MudIconButton Size="Size.Small"
            Icon="@(Node.IsPinned ? Icons.Material.Filled.PushPin : Icons.Material.Outlined.PushPin)"
            Color="@(Node.IsPinned ? Color.Primary : Color.Default)" OnClick="TogglePin" title="Pin" />
        <MudIconButton Size="Size.Small"
            Icon="@(Node.IsFavorite ? Icons.Material.Filled.Star : Icons.Material.Outlined.StarBorder)"
            Color="@(Node.IsFavorite ? Color.Warning : Color.Default)" OnClick="ToggleFav" title="Favorite" />
        @if (Node.State != NodeState.Archived)
        { <MudIconButton Size="Size.Small" Icon="@Icons.Material.Filled.Archive" OnClick="Archive" title="Archive" /> }
        else
        { <MudIconButton Size="Size.Small" Icon="@Icons.Material.Filled.Unarchive" OnClick="Restore" title="Restore" /> }
    </div>
</section>
```
Handlers:
```csharp
private async Task SetKind(NodeKind k){ if(k==Node.Kind) return; await NodeService.PromoteAsync(Node.NodeId,k); await Notify(); }
private async Task SetState(NodeState s){ if(s==Node.State) return; await NodeService.SetStateAsync(Node.NodeId,s); await Notify(); }
private async Task TogglePin(){ await NodeService.TogglePinAsync(Node.NodeId); await Notify(); }
private async Task ToggleFav(){ await NodeService.ToggleFavoriteAsync(Node.NodeId); await Notify(); }
private async Task Archive(){ await NodeService.ArchiveAsync(Node.NodeId); Snackbar.Add("Archived",Severity.Info); await Notify(); }
private async Task Restore(){ await NodeService.RestoreAsync(Node.NodeId); await Notify(); }
```

- [ ] **Step 4: Compose the REUSED panels unchanged + reserved AI slots.** Pass only documented params; forward `OnChanged` to `Notify` so counts refresh.
```razor
<section class="prop-section">
    <TagAutocomplete AllTags="_allTags" SelectedTagIds="_selectedTagIds"
                     SelectedTagIdsChanged="OnTagsChanged" />
    <TagChips Tags="Node.Tags" />
</section>
<section class="prop-section">
    <ConnectionsPanel NodeId="Node.NodeId" OnChanged="Notify" />
</section>
<section class="prop-section">
    <ActionsPanel NodeId="Node.NodeId" OnChanged="Notify" />
</section>
<section class="prop-section">
    <CollectionAssignmentPanel NodeId="Node.NodeId" OnChanged="Notify" />
</section>
<section class="prop-section prop-ai" data-ai-slot="suggested-links"></section>
<section class="prop-section prop-ai" data-ai-slot="summary"></section>
```
Tag persistence: `TagAutocomplete` only edits the working list; wire `OnTagsChanged(List<int> ids)` to persist via the existing node-update path — reload the node with `GetByIdAsync`, and call `NodeService.UpdateAsync(node, ids)` (mirrors how `NodeDetail.SaveEdit` persists tag ids), then `Notify()`. Confirm this matches how `NodeEditor` persists tags before finalizing.

- [ ] **Step 5: Build.**
```bash
dotnet build "C:/Users/capnb/source/repos/Nook/Nook.sln"
```
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 6: Manual verification.** `dotnet watch --project "C:/Users/capnb/source/repos/Nook/Nook.csproj"`, open http://localhost:5176. Until Task 4 renders it on NodePage, temporarily mount `<PropertiesPanel Node="_node" OnChanged="LoadAsync" />` in `NodeDetail.razor` (revert after). Observe: (a) clicking a Kind chip re-classifies the node (badge/state reflect after reload); (b) changing State via the select persists across refresh; (c) pin/favorite toggles flip and persist; (d) adding a connection/action/collection via the reused panels still works exactly as before (no regression); (e) the collapse chevron hides the body; (f) `data-ai-slot` regions render empty (inspect DOM).

- [ ] **Step 7: Commit.**
```bash
git -C "C:/Users/capnb/source/repos/Nook" add -A
git -C "C:/Users/capnb/source/repos/Nook" commit -m "feat(node-page): PropertiesPanel composing reused panels + kind/state/pin/fav controls"
```

---

### Task 26: Build BacklinksPanel (grouped by relation label) + ActivityStrip

*Stream: Node Page Assembly · orderHint 74 · Depends on: Task 24 (InlineTitleEditor + NodeHeader + ObjectTypeBadge)*

> **Reconciliation:** `<ObjectTypeBadge Kind=... Dense="true" />` binds the canonical Task 11 badge (its contract is extended to expose `Dense`); reads `NodeConnections.Backlinks`/`Connection` from the existing `IRelationService`.

**Files:**
- Create: `Components/Nodes/BacklinksPanel.razor` — renders `GetConnectionsAsync().Backlinks` grouped by `Connection.Label`, each row an `ObjectTypeBadge` + link to `/nodes/{OtherNodeId}`.
- Create: `Components/Nodes/ActivityStrip.razor` — merges `ActivityService.GetForNodeAsync` and `EventService.GetEventsForNodeAsync` into one time-ordered strip.
- Test: NONE (Blazor components). Verify via build + dotnet watch.
- Read first: `Services/RelationModels.cs` (Connection record: `OtherNodeId`, `OtherTitle`, `OtherKind`, `Label`, `IsOutgoing`, `Note`; `NodeConnections.Backlinks`), `Services/IRelationService.cs` (`GetConnectionsAsync(int) : Task<NodeConnections>`), `Services/IActivityService.cs` (`GetForNodeAsync(string userId, int nodeId, int? take)`), `Services/IEventService.cs` (`GetEventsForNodeAsync(int) : Task<List<Node>>`), `Models/ActivityLog.cs` (Timestamp/Type/Detail), `Components/Pages/NodeDetail.razor` lines 200-255 (existing activity/events rendering to mirror).

**Interfaces:**
Consumes:
- `IRelationService.GetConnectionsAsync(int nodeId) : Task<NodeConnections>`; use `.Backlinks` (IReadOnlyList<Connection>).
- `IActivityService.GetForNodeAsync(string userId, int nodeId, int? take = null) : Task<List<ActivityLog>>` — needs `Node.UserId` for the userId arg.
- `IEventService.GetEventsForNodeAsync(int nodeId) : Task<List<Node>>` — each result's `EventDetails.OccurredAt` supplies the timestamp.
- `ObjectTypeBadge` (Task 1) with `Kind = Connection.OtherKind`.
- `NodeUi.Format(DateTime?)`, `NodeUi.Icon`.
Produces:
- `BacklinksPanel` params: `[Parameter] int NodeId` (required), `[Parameter] int? RefreshToken` (optional — change it to force reload after a mutation).
- `ActivityStrip` params: `[Parameter] int NodeId` (required), `[Parameter] string UserId` (required — from `Node.UserId`), `[Parameter] int Take = 12`.

**Steps:**

- [ ] **Step 1: Confirm on the shared branch.**
```bash
git -C "C:/Users/capnb/source/repos/Nook" checkout feature/node-page-assembly
```

- [ ] **Step 2: Create `Components/Nodes/BacklinksPanel.razor`.** Load connections, group backlinks by `Label`, render badge + title link.
```razor
@using Nook.Models
@using Nook.Services
@inject IRelationService RelationService

<section class="backlinks">
    <MudText Typo="Typo.subtitle2" Class="mb-2">Backlinks</MudText>
    @if (_backlinks.Count == 0)
    {
        <MudText Typo="Typo.body2" Class="mud-text-secondary">No backlinks yet.</MudText>
    }
    else
    {
        @foreach (var group in _backlinks.GroupBy(b => b.Label).OrderBy(g => g.Key))
        {
            <div class="backlinks__group">
                <MudText Typo="Typo.overline">@group.Key</MudText>
                @foreach (var c in group)
                {
                    <MudLink Href="@($"/nodes/{c.OtherNodeId}")" Class="backlinks__row">
                        <ObjectTypeBadge Kind="c.OtherKind" Dense="true" />
                        <span>@c.OtherTitle</span>
                    </MudLink>
                }
            </div>
        }
    }
</section>

@code {
    [Parameter, EditorRequired] public int NodeId { get; set; }
    [Parameter] public int? RefreshToken { get; set; }
    private IReadOnlyList<Connection> _backlinks = System.Array.Empty<Connection>();
    private int _loadedFor = -1; private int? _loadedToken;

    protected override async Task OnParametersSetAsync()
    {
        if (_loadedFor == NodeId && _loadedToken == RefreshToken) return;
        var conns = await RelationService.GetConnectionsAsync(NodeId);
        _backlinks = conns.Backlinks;
        _loadedFor = NodeId; _loadedToken = RefreshToken;
    }
}
```

- [ ] **Step 3: Create `Components/Nodes/ActivityStrip.razor`.** Merge activity-log rows and referencing-event rows into one descending-time list. Keep it a read-only strip.
```razor
@using Nook.Models
@using Nook.Services
@inject IActivityService ActivityService
@inject IEventService EventService

<section class="activity-strip">
    <MudText Typo="Typo.subtitle2" Class="mb-2">Activity</MudText>
    @if (_rows.Count == 0)
    {
        <MudText Typo="Typo.body2" Class="mud-text-secondary">No activity yet.</MudText>
    }
    else
    {
        @foreach (var r in _rows)
        {
            <div class="activity-strip__row">
                <MudIcon Icon="@r.Icon" Size="Size.Small" />
                <span class="activity-strip__when">@NodeUi.Format(r.When)</span>
                <span>@r.Text</span>
            </div>
        }
    }
</section>

@code {
    [Parameter, EditorRequired] public int NodeId { get; set; }
    [Parameter, EditorRequired] public string UserId { get; set; } = "";
    [Parameter] public int Take { get; set; } = 12;

    private record Row(DateTime? When, string Text, string Icon);
    private List<Row> _rows = new();

    protected override async Task OnParametersSetAsync()
    {
        var logs = await ActivityService.GetForNodeAsync(UserId, NodeId, Take);
        var events = await EventService.GetEventsForNodeAsync(NodeId);
        _rows = logs.Select(l => new Row(l.Timestamp,
                    l.Detail is null ? l.Type.ToString() : $"{l.Type} ({l.Detail})",
                    Icons.Material.Filled.History))
            .Concat(events.Select(e => new Row(e.EventDetails?.OccurredAt,
                    $"Referenced by event: {e.Title}", Icons.Material.Filled.Event)))
            .OrderByDescending(r => r.When)
            .Take(Take)
            .ToList();
    }
}
```
VERIFY the `ActivityLog.Timestamp` and `ActivityLog.Type`/`.Detail` member names against `Models/ActivityLog.cs` before finalizing (NodeDetail.razor line 216 uses `log.Timestamp`, `log.Type`, `log.Detail` — mirror exactly).

- [ ] **Step 4: Build.**
```bash
dotnet build "C:/Users/capnb/source/repos/Nook/Nook.sln"
```
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 5: Manual verification.** `dotnet watch --project "C:/Users/capnb/source/repos/Nook/Nook.csproj"`, open http://localhost:5176. Temporarily mount `<BacklinksPanel NodeId="_node.NodeId" />` and `<ActivityStrip NodeId="_node.NodeId" UserId="@_node.UserId" />` in `NodeDetail.razor` (revert after). On a node that is the TARGET of a relation from another node, observe: (a) that source appears under BacklinksPanel grouped by the relation label, with the correct kind badge, and the link navigates to it; (b) editing the node (rename/kind change) or logging an event referencing it produces new rows in ActivityStrip in descending time order. Confirm no exception when a node has zero backlinks/activity (empty-state text shows).

- [ ] **Step 6: Commit.**
```bash
git -C "C:/Users/capnb/source/repos/Nook" add -A
git -C "C:/Users/capnb/source/repos/Nook" commit -m "feat(node-page): BacklinksPanel grouped by relation label + ActivityStrip"
```

---

### Task 27: Assemble NodePage (/nodes/{Id:int}) grid + retire NodeDetail

*Stream: Node Page Assembly · orderHint 76 · Depends on: Task 24 (NodeHeader), Task 25 (PropertiesPanel), Task 26 (BacklinksPanel/ActivityStrip), Task 23 (EditorHost), Task 13 (WorkspaceState.NoteVisitedAsync)*

> **Reconciliation:** EditorHost's body parameter is `InitialBody` (Task 23), NOT `InitialMarkdown` — the `<EditorHost>` tag below is fixed to `InitialBody`. `WorkspaceState.NoteVisitedAsync` is Task 13; child-component contracts are Tasks 24/25/26. `WorkspaceState` is the same scoped instance the shell cascades, so `@inject` is valid. Uses `Node.NodeId`.

**Files:**
- Create: `Components/Nodes/NodePage.razor` with `@page "/nodes/{Id:int}"` — the single owner of that route; grid of NodeHeader + EditorHost + PropertiesPanel + BacklinksPanel + ActivityStrip.
- Delete: `Components/Pages/NodeDetail.razor` (and its `.razor.css`/`.razor.js` if any) — it currently owns `@page "/nodes/{Id:int}"`; two owners = ambiguous-route runtime error.
- Modify: none of the child components; NodePage only orchestrates load + reload + WorkspaceState.
- Test: NONE (Blazor page). Verify via build (ambiguous-route surfaces at build/startup) + dotnet watch end-to-end.
- Read first: `Components/Pages/NodeDetail.razor` (its `LoadAsync`/`RefreshCounts` orchestration to port), `Components/Nodes/EditorHost.razor` (its param contract — likely `[Parameter] int NodeId` and `[Parameter] string? InitialMarkdown`; read the real signature), `Services/WorkspaceState.cs` (`NoteVisitedAsync(int)`), `Services/INodeService.cs` (`GetByIdAsync`).

**Interfaces:**
Consumes:
- `INodeService.GetByIdAsync(int) : Task<Node?>`.
- `WorkspaceState.NoteVisitedAsync(int nodeId) : Task` (scoped service cascaded from WorkspaceShell; inject or accept as `[CascadingParameter]` per how the Workspace stream exposes it — read `Services/WorkspaceState.cs`).
- `NodeHeader` (`Node`, `OnTitleChanged`), `PropertiesPanel` (`Node`, `OnChanged`, `Collapsed`/`CollapsedChanged`), `BacklinksPanel` (`NodeId`, `RefreshToken`), `ActivityStrip` (`NodeId`, `UserId`, `Take`), `EditorHost` (its real param set — pass `NodeId` and initial body/markdown from `Node.Body`).
Produces:
- Route `/nodes/{Id:int}` (sole owner). No new public component API — it is a page.
- Behavioral contract: after any child `OnChanged`/title edit, NodePage reloads the node and bumps a `RefreshToken` so BacklinksPanel/ActivityStrip re-query; on load it calls `WorkspaceState.NoteVisitedAsync(Id)`.

**Steps:**

- [ ] **Step 1: Confirm on the shared branch.**
```bash
git -C "C:/Users/capnb/source/repos/Nook" checkout feature/node-page-assembly
```

- [ ] **Step 2: Delete `NodeDetail.razor` FIRST** so the route is free (avoids the ambiguous-route the repo hit before — see commit f62dd8c). Remove sidecar files too if present.
```bash
git -C "C:/Users/capnb/source/repos/Nook" rm Components/Pages/NodeDetail.razor
```
Expected: file staged for deletion. (If a `NodeDetail.razor.css` exists, `git rm` it as well.)

- [ ] **Step 3: Create `Components/Nodes/NodePage.razor`.** Port the load/reload skeleton from NodeDetail (loading + not-found guards), then lay out the grid and wire reload + RefreshToken + WorkspaceState.
```razor
@page "/nodes/{Id:int}"
@using Nook.Models
@using Nook.Services
@inject INodeService NodeService
@inject WorkspaceState Workspace

<PageTitle>@(_node?.Title ?? "Node") · Nook</PageTitle>

@if (_loading)
{
    <MudProgressLinear Indeterminate="true" Color="Color.Primary" />
}
else if (_node is null)
{
    <MudAlert Severity="Severity.Error">Node not found.</MudAlert>
}
else
{
    <div class="node-page">
        <div class="node-page__main">
            <NodeHeader Node="_node" OnTitleChanged="OnTitleChanged" />
            <EditorHost NodeId="_node.NodeId" InitialBody="@(_node.Body ?? string.Empty)" />
            <BacklinksPanel NodeId="_node.NodeId" RefreshToken="_refresh" />
            <ActivityStrip NodeId="_node.NodeId" UserId="@_node.UserId" />
        </div>
        <PropertiesPanel Node="_node" OnChanged="ReloadAsync" @bind-Collapsed="_propsCollapsed" />
    </div>
}

@code {
    [Parameter] public int Id { get; set; }
    private Node? _node;
    private bool _loading = true;
    private bool _propsCollapsed;
    private int _refresh;

    protected override async Task OnParametersSetAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        _loading = true;
        _node = await NodeService.GetByIdAsync(Id);
        if (_node is not null) await Workspace.NoteVisitedAsync(Id);
        _loading = false;
    }

    private async Task ReloadAsync()
    {
        _node = await NodeService.GetByIdAsync(Id);
        _refresh++;              // force Backlinks/Activity re-query
    }

    private async Task OnTitleChanged(string _) => await ReloadAsync();
}
```
**(Reconciliation applied:** the `<EditorHost>` tag uses `InitialBody` — EditorHost's real body param (Task 23) — not `InitialMarkdown`.) Verify whether `WorkspaceState` is injected (`@inject`) or supplied as a `[CascadingParameter]` from WorkspaceShell, and match that (both resolve to the same scoped instance).

- [ ] **Step 4: Add `Components/Nodes/NodePage.razor.css`** for the two-column grid that collapses on narrow screens (page body must never scroll horizontally):
```css
.node-page{display:grid;grid-template-columns:minmax(0,1fr) 320px;gap:1.5rem;align-items:start;}
.node-page__main{min-width:0;display:flex;flex-direction:column;gap:1.25rem;}
@media (max-width:960px){.node-page{grid-template-columns:1fr;}}
```

- [ ] **Step 5: Build — this is where an ambiguous route or a missing child param surfaces.**
```bash
dotnet build "C:/Users/capnb/source/repos/Nook/Nook.sln"
```
Expected: `Build succeeded. 0 Error(s)`. Then grep to prove the route has exactly one owner:
```bash
git -C "C:/Users/capnb/source/repos/Nook" grep -n 'nodes/{Id:int}'
```
Expected: a single hit, in `Components/Nodes/NodePage.razor`.

- [ ] **Step 6: End-to-end manual verification (the acceptance behaviors).** `dotnet watch --project "C:/Users/capnb/source/repos/Nook/Nook.csproj"`, open http://localhost:5176, navigate to `/nodes/{someId}`. Confirm ALL of:
  1. Page loads with header, editor, properties rail, backlinks, activity — no ambiguous-route error.
  2. Edit body in the editor → after the debounce it saves (reload page; body persists) — exercises `EditorHost → SaveBodyAsync`.
  3. Type `[[Some New Title]]` in the body → a node is created and, on reload, THIS node shows in the target's BacklinksPanel (mentions) — exercises WikiLink reconcile.
  4. Toggle a checkbox/action in the editor or ActionsPanel → the ActionItem's done-state flips and persists.
  5. Change Kind/State/pin/favorite in PropertiesPanel → header badge/chip update after reload and persist.
  6. Rename the title inline → "Saved" indicator, persists on refresh, ActivityStrip gains a row.
  7. Focus lands on the static h1 after navigation (not inside the contenteditable).
  8. Navigating here adds the node to the workspace recents (visible once the sidebar recents from the Workspace stream are wired) — at minimum confirm `NoteVisitedAsync` runs without error.

- [ ] **Step 7: Commit.**
```bash
git -C "C:/Users/capnb/source/repos/Nook" add -A
git -C "C:/Users/capnb/source/repos/Nook" commit -m "feat(node-page): assemble NodePage grid; retire NodeDetail (single route owner)"
```

---

## Self-Review

### (1) Spec-coverage checklist — each Phase-1 spine item → Task number(s)

| Phase-1 spine item | Task(s) |
|---|---|
| Adaptive workspace shell (CSS-grid, rail/sidebar/topbar) | **Task 13** (WorkspaceState/ThemeState) · **Task 14** (WorkspaceShell) · **Task 15** (GlobalRail/TopBar/Breadcrumbs) · **Task 16** (WorkspaceSidebar/SidebarNodeLink) · **Task 17** (swap DefaultLayout) |
| ⌘K command palette | **Task 18** (CommandRegistry) · **Task 19** (overlay + keyboard nav + debounced search) · **Task 20** (wire Actions: capture/create/theme/go-to/⌘↵) · **Task 21** (retire app-bar search, keep /search) |
| Rich Node page + client editor | **Task 27** (NodePage assembly) · **Task 22** (vendored TipTap bundle) · **Task 23** (EditorHost + JS contract) · **Task 1** (interop spike that de-risks 22/23) |
| Wikilinks → mentions | **Task 6** (WikiLinkService resolve + reconcile) · **Task 23** (`ExtractWikiTitles` + `ReconcileAsync` on debounced save) |
| Backlinks | **Task 26** (BacklinksPanel, grouped by relation label) |
| Properties panel | **Task 25** (PropertiesPanel composing reused panels + kind/state/pin/fav) |
| Visual system / tokens / fonts | **Task 7** (nook-tokens.css) · **Task 8** (self-hosted fonts) · **Task 10** (NookTheme MudTheme) · **Task 11** (ObjectTypeBadge) · **Task 12** (theme-interop) · kind-accent map **Task 4** (= duplicate **Task 9**) |
| UserPreference migration | **Task 5** (model + `IUserPreferenceService` + `AddUserPreference` migration) |
| Retire NavMenu / NodeDetail | **Task 17** (delete NavMenu + MainLayout) · **Task 27** (delete NodeDetail, single route owner) |

Supporting/enabling tasks (not standalone spine items but required by the above): **Task 2** (NodeFilter.Take → palette/wikilink/editor search caps), **Task 3** (SaveBodyAsync → editor persistence), **Task 24** (InlineTitleEditor + NodeHeader static-`<h1>` focus target), and **Task 26**'s ActivityStrip.

Interface threads verified consistent end-to-end: `SaveBodyAsync` (Task 3 → Task 23), `IUserPreferenceService` (Task 5 → Task 13), `WorkspaceState` (Task 13 → Tasks 14/15/16/19/20/27), `NodeFilter.Take` (Task 2 → Tasks 6/19/23), the EditorHost JS contract (Task 23 → Task 27, with `InitialMarkdown` reconciled to the producer's `InitialBody`), and `Command`/`CommandRegistry.Match` (Task 18 → Tasks 19/20). Cross-stream duplicate producers were reconciled to one canonical owner each: `NodeUi.KindAccent` (Task 4 owns; Task 9 verify/no-op), `NookTheme` (Task 10 owns; Task 14 reuses), `ObjectTypeBadge` (Task 11 owns; Task 24 reuses; `Dense` alias added for Task 26), and the theme switch (global `__nookTheme.setTheme` wired by Tasks 13/14; `theme-interop.js` from Task 12 is the standalone equivalent). The `Node.Id`/`Node.NodeId` mismatch is reconciled to the schema-owner PK `Node.NodeId`.

### (2) Placeholders

No placeholders remain — every one of the 27 tasks specifies concrete Files, Interfaces, and executable `- [ ]` steps ending in a real build/test/commit. The only intentionally deferred surfaces are explicitly scoped post-spine follow-ups, not unfilled gaps: the sidebar drag **source** (Task 16 ships the drop target + click nav) and the two empty `data-ai-slot` regions reserved in PropertiesPanel (Task 25).

### (3) Editor-interop spike ordering

Confirmed: the editor-interop spike is **Task 1** (orderHint 10) and precedes the editor work — it proves the dynamic `.razor.js` import + `DotNetObjectReference` roundtrip + `IAsyncDisposable`/`JSDisconnectedException` teardown pattern that **Task 22** (bundle) and **Task 23** (EditorHost) reuse verbatim; Task 23's final step retires the spike once EditorHost proves the same pattern in real use.

### Gap scan

No missing tasks: every Phase-1 spine item in the checklist maps to at least one task, and each named cross-stream contract has both a producer and its consumer(s) present in the plan.