# Nookryptex — Design Spec

> Date: 2026-07-03. A cryptex-styled faceted browser for the Nook knowledge graph. Reuses the existing Node / Tag / Collection / Relation model and services — no schema or architecture changes. Companion prototype iterations live under `.superpowers/brainstorm/` (git-ignored).

## Summary

A new page at **`/nookryptex`** that presents the user's graph as a five-wheel cryptex. Each wheel is a facet (**Kind · Tag · Collection · State · People**) showing all its values. Clicking a value on any wheel filters the others (cross-facet narrowing) and updates a top band that shows **The code** (active filters), the **Locked** node (when narrowed to one, or a focused match), and the live **Matching nodes** list. A **new-item line** lets the user add a node that inherits the currently dialed-in code. Nav buttons link to the other Nook pages. Filtering is computed in-memory from a one-shot dataset load, so it feels instant.

## Goals / Non-goals

**Goals**
- Browse the graph by turning facet wheels; selecting on any wheel filters the others.
- Drill into a node's detail without leaving the page; "Open node →" goes to `/nodes/{id}`.
- Add a node inline that inherits the active code (kind/tag/collection/state/person).
- Keep it snappy and simple — MudBlazor + Blazor Interactive Server, minimal JS.

**Non-goals**
- No graph/canvas visualization, no new persistence, no changes to the Node model.
- No drag-to-spin physics; wheels are click-to-select scrollable columns.
- The People wheel filters by *relationship to a Person node* — it does not introduce a new "people" data concept.

## Layout (approved)

Top to bottom, full page (max-width ~1280px, dark slate + brass "cryptex" theme):

1. **Title** "Nookryptex" + one-line hint.
2. **Nav buttons** — pill links to `/today`, `/inbox`, `/all`, `/people`, `/collections`, `/actions`, `/events`, `/timeline`.
3. **Top band** — three equal-height columns:
   - **The code** — active facet chips (each removable), the "N nodes align" tally, and Reset.
   - **Locked** — the focused/locked node card (icon, title, kind·state, tags, body preview, counts for collections/people/tags, "Open node →"); placeholder prompt when nothing is narrowed/focused.
   - **Matching nodes** — scrollable list of all matches; click a row to focus it in the Locked panel.
4. **New-item line** — a brass input row: a title field, a live "inherits …" preview of the current code, and an **Add** button (Enter also submits).
5. **Full-width cryptex** — five vertical wheels side by side inside a brass-capped cylinder with a center selection "window". Each wheel: sticky label, a vertically-scrolling list of values with per-value match counts; the selected value highlights and (progressive enhancement) scrolls to the center window; values with zero matches under the current code are dimmed.

Responsive: on narrow screens the top band and cryptex wheels wrap/stack naturally (MudGrid / flex-wrap). No custom drag-and-drop.

## Facets & filtering semantics

The five wheels and how a node supplies each facet value:

| Wheel | Value source per node |
|---|---|
| **Kind** | `Node.Kind` (single) |
| **Tag** | tag names via `NodeTags` |
| **Collection** | active collection names the node is a member of (`CollectionMembership` → `Collection.Node.Title`) |
| **State** | `Node.State` (Inbox / Active / Archived) |
| **People** | titles of **Person**-kind nodes related to this node via any `NodeRelation` (either direction) |

**Selection state**: at most one selected value per wheel (`sel[kind|tag|coll|state|people]`, each nullable). Clicking a selected value clears it.

**Match rule**: a node matches when, for every wheel that has a selection, the node contains that value (Kind/State equality; Tag/Collection/People membership).

**Per-value counts**: for wheel `R`, value `v`, the count = number of nodes that match all selections on wheels *other than* `R` **and** contain `v` on `R`. Values with count 0 are dimmed and not selectable (unless already selected). The tally = total nodes matching all selections.

This logic is pure and lives in a testable helper (`CryptexEngine`), independent of the DB.

## Data flow

1. On page load, `ICryptexService.GetDatasetAsync()` returns a compact projection of **all the current user's nodes** (including archived, so the State wheel can reach Archived):

   ```
   record CryptexNode(
     int NodeId, string Title, NodeKind Kind, NodeState State,
     string? BodyPreview,               // first ~160 chars, for the Locked panel
     IReadOnlyList<string> Tags,
     IReadOnlyList<string> Collections, // active collection titles
     IReadOnlyList<string> People);     // related Person-node titles
   ```

   Built with a few user-scoped queries (nodes; node-tags; collection memberships joined to collection nodes; relations joined to Person-kind endpoints), assembled in memory. Personal-scale data (hundreds of nodes) makes a full load cheap and enables instant client-side filtering.

2. The page holds the dataset + selection state and re-derives wheels/panels on each click via `CryptexEngine` — no round-trips while turning wheels.

3. **Open node →** navigates to `/nodes/{id}` (existing detail page).

4. **Add node** calls `ICryptexService.AddNodeWithCodeAsync(title, code)`, then refreshes the dataset (or optimistically inserts) and focuses the new node.

## New-item ("inherit the code") behavior

`AddNodeWithCodeAsync(string title, CryptexCode code)` where `CryptexCode` carries the current selections. It:
1. Creates a `Node` with `Kind = code.Kind ?? Unclassified`, `State = code.State ?? Inbox`, `Title` (trimmed). (Mirrors quick-capture defaults when no kind/state is dialed in.)
2. If `code.Tag` is set → `TagService.GetOrCreateAsync(tag)` then assign to the node.
3. If `code.Collection` is set → resolve the existing collection by title (wheel values come from existing collections) and `CollectionService.AddMemberAsync`.
4. If `code.Person` is set → resolve the existing Person node by title and create a `NodeRelation` (default type **"associated with"**, symmetric) between the new node and that person. *(The People wheel is a relationship facet, so stamping a person deliberately creates a relation — this is intended, not an incidental side-effect.)*
5. All operations are user-scoped and reuse existing ownership guards (a user can't tag/collection/relate to another user's objects). Best-effort sequential; failures surface via snackbar and never lose the created node.

Returns the new `NodeId`. The page marks it "Just added" (green) in the matches list and locks it in the detail panel.

## Components & files

New:
- `Components/Pages/Nookryptex.razor` — the page: loads the dataset, holds selection + focus state, renders the top band + add line, orchestrates add/reset/focus.
- `Components/Shared/CryptexCylinder.razor` — renders the five wheels from `(dataset, selection)` and raises `OnSelect(ring, value)`.
- `Components/Shared/CryptexWheel.razor` — one wheel: label, value rows with counts, dim/selected styling, click.
- `Services/CryptexModels.cs` — `CryptexNode`, `CryptexCode`, `CryptexSelection`.
- `Services/CryptexEngine.cs` — pure filtering/count helpers (static, no DB).
- `Services/ICryptexService.cs` / `CryptexService.cs` — `GetDatasetAsync()`, `AddNodeWithCodeAsync()`.
- `wwwroot/nookryptex.js` (or a small `<script>` module) — one function `scrollToCenter(el)` for the rotate-into-window effect, called via `IJSRuntime`. Pure progressive enhancement: without it, the selected value still highlights.

Changed:
- `Program.cs` — register `ICryptexService`.
- `Components/Layout/NavMenu.razor` — add a **Nookryptex** link.
- The cryptex theme CSS is scoped to the page (a `nookryptex` container class) so it doesn't affect the rest of the app; reuse `NodeUi.Icon` for kind icons where practical.

The three top panels (code / locked / matches) live inline in `Nookryptex.razor` — they're tightly coupled to selection/focus state and don't warrant separate components.

## Interaction rules

- Turning a wheel updates the code, locked panel, matches, and the add-line preview in place; the route stays `/nookryptex`.
- Narrowing to exactly one match auto-locks that node; clicking any match focuses it (focus overrides auto-lock until the code changes).
- Removing a code chip or Reset clears selections; a Node may match zero wheels (everything in view).
- "All / Inbox / Unassigned" remain system views reached via nav buttons — they are not wheels.
- People/Collection wheels never *create* data on selection; only the explicit **Add** stamps data.

## Testing

Pure logic (`CryptexEngine`, no DB):
- Match rule across multiple selected wheels; empty selection = all nodes.
- Per-value counts ignore their own wheel; dim when count 0.
- Symmetric behavior: selecting on wheel A then B equals B then A.

Service (`CryptexService`, EF InMemory):
- `GetDatasetAsync` returns only the current user's nodes with correct facet lists (tags, active collections, related people); excludes another user's nodes.
- `AddNodeWithCodeAsync` stamps Kind/State and applies tag, collection membership, and a person relation from the code; defaults to Unclassified/Inbox with no code.
- Cross-user safety: cannot stamp another user's collection or person (reuses existing guards; membership/relation not created).

Build + runtime:
- Solution builds; `/nookryptex` renders authenticated with zero server-side exceptions (smoke via the existing login-then-GET pattern); nav buttons resolve; "Open node →" links to `/nodes/{id}`.

## What we are NOT building
Graph/canvas visualization; spin-physics wheels; new persistence or schema; server round-trips per wheel turn; multi-select per wheel (single selection per wheel is the model); auto-relations on collection membership.

## Open defaults (chosen, overridable)
- New-node **Kind** defaults to **Unclassified** and **State** to **Inbox** when those wheels aren't set (matches quick-capture).
- Person stamping uses the **"associated with"** system relation type.
- The dataset includes **archived** nodes so the State wheel can reach Archived; the initial view has no wheel set (everything visible).
