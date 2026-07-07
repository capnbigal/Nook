# Nook Redesign — Authoritative Design Document

**Author:** Lead UX Architect · **Audience:** implementing engineer · **Status:** approved direction, buildable
**Thesis:** Nook already has the right *model* — a Node-centered knowledge graph with real services. It has the wrong *surface*. This document turns the surface into a second brain without disturbing the graph beneath it.

---

## 1. Current-UI audit — what we have, and why it fights the vision

The app today is a competent MudBlazor CRUD admin sitting on top of a genuinely good graph model. Concretely:

- **The shell (`Components/Layout/MainLayout.razor`)** is the stock Material three-piece: `MudAppBar` (menu button, "Nook" wordmark, an enter-to-navigate `MudTextField` search wired to `OnSearchKeyUp` → `/search?q=`, a `+` `MudIconButton` that *navigates* to `/capture`, and a dark-mode toggle backed by a **private** `_isDarkMode` bool), a persistent `MudDrawer` (`Variant.Responsive`, `ClipMode.Always`) holding the nav, and `MudMainContent > MudContainer MaxWidth.Large`. The theme is literally `private readonly MudTheme _theme = new();` — the unstyled enterprise default. `Routes.razor` binds every page to this layout (`DefaultLayout="typeof(Layout.MainLayout)"`) and focuses `Selector="h1"` on navigation.
- **Navigation (`NavMenu.razor`)** is a flat, static `MudNavMenu` of ~19 hand-listed `MudNavLink`s in four divider groups plus a Tags accordion. It is a *sitemap*, not a workspace. It never surfaces the user's own content, even though the data to do so already exists (`Node.IsPinned`, `Node.IsFavorite`, `INodeService.GetPinnedAsync`/`GetFavoritesAsync`, `ICollectionService.GetCollectionsAsync`).
- **The node page (`NodeDetail.razor`, `@page "/nodes/{Id:int}"`)** is the sharpest miss. It renders the document as a **three-row, six-box `MudGrid` dashboard**. `Node.Body` is shown as `<MudText Style="white-space:pre-wrap">` — **plain text, no Markdown, no wiki-links, no formatting**. Editing swaps the whole page for a `NodeEditor` **form**. Backlinks are *computed* (`RelationService.GetConnectionsAsync(Id)` already returns `Backlinks`) but only rendered as a number in an "At a glance" stat and a few "relationship notes" — there is no backlinks surface. The document — the thing the user came to read and write — is the smallest, least prominent element on its own page.
- **Foundation (`App.razor`)** loads Roboto from the Google Fonts CDN (`preconnect` + stylesheet), MudBlazor CSS/JS, one hand-written global `js/nookryptex.js`, and an `<ImportMap>`. Render mode is global `InteractiveServer` with a static-SSR fallback (`AcceptsInteractiveRouting() ? InteractiveServer : null`). There is no npm/vite/webpack/Tailwind. One collocated ES module already exists — `ReconnectModal.razor.js` — but it is loaded via a *static* `<script type="module">` tag, **not** the dynamic `import()` + `DotNetObjectReference` round-trip the editor needs. ESM collocation is therefore established; the dynamic-import interop pattern is standard Blazor but not yet exercised here, so a throwaway spike proves it first (§6, Phase 1(a)).

Why it fights the vision: the vision is *"a warm, object-typed second brain where writing and structuring are the same act."* The current UI is *form-first* (capture and edit are modal forms), *chrome-heavy* (a persistent drawer + app bar frame every page), *type-blind* (kinds carry an icon via `NodeUi.Icon` but **no color** — `NodeUi` has `StateColor` and `PriorityColorHex` but nothing per-`NodeKind`), and *keyboard-mute* (no palette; search is a full-page round-trip). Nothing is wrong with the model; everything is wrong with the frame around it.

---

## 2. Usability issues (prioritized)

| # | Sev | Issue | Rooted in |
|---|-----|-------|-----------|
| 1 | **P0** | Writing is second-class: `Node.Body` renders as pre-wrapped plain text; editing is a separate `NodeEditor` form; no inline title edit, wiki-links, or backlinks surface. | `NodeDetail.razor` L48–55, edit-mode form |
| 2 | **P0** | Navigation ignores your graph — a static 19-link menu, no favorites/pinned/recents, despite the services existing. | `NavMenu.razor`; unused `GetPinnedAsync`/`GetFavoritesAsync` |
| 3 | **P0** | No fast capture or jump. Search = type + Enter → `/search` full page; `+` = navigate to `/capture`. Every quick action is a page load. | `MainLayout.razor` `OnSearchKeyUp`, `+` `Href="/capture"` |
| 4 | **P1** | The node page scatters one document across six `MudPaper` boxes, burying content beneath metadata. | `NodeDetail.razor` 3× `MudGrid` |
| 5 | **P1** | Object *type* is under-expressed — icon only, no color, no consistent badge. You can't scan a list and read type by color. | `NodeUi.Icon` exists; no `KindColor` |
| 6 | **P1** | Enterprise Material default look (empty `MudTheme`, Roboto, 4px corners) contradicts the "playful & tactile" target. | `MainLayout` `_theme = new()`; `App.razor` Roboto CDN |
| 7 | **P2** | No keyboard-first flows; power use is all mouse + navigation. | no global key interop |
| 8 | **P2** | Persistent drawer + app bar consume horizontal space on every page, including the reading surface. | `MudDrawer` + `MudContainer` |

---

## 3. UX strategy & north star

**North star:** *Nook is a second brain, not a database of records.* A person opens Nook to think — to write a note that quietly wires itself into everything related, to jump to any idea in two keystrokes, to see their own knowledge as colorful, tactile objects rather than rows in a grid. The model already supports this (a `Node` is a page; relations are backlinks; collections, actions, and events are behavior around it). Our job is to make the surface *feel* like thinking.

**Product principles**

1. **The document is the surface.** The page *is* the writing. Metadata gets out of the way (a collapsible rail), never the reverse.
2. **Navigation emerges from your graph.** The sidebar is your Favorites (`IsFavorite`), Pinned (`IsPinned`), Collections, and Recents — not a fixed sitemap. Fixed views are demoted to a slim rail and the palette.
3. **Every object wears its type.** One `ObjectTypeBadge` (kind color dot + `NodeUi.Icon` + optional label) everywhere: sidebar, breadcrumbs, palette, cards, backlinks. Color is never the *only* signal (icon + label cover colorblindness).
4. **Keyboard-first, capture-anywhere.** `Cmd/Ctrl-K` is the primary way to search, jump, create, and act. Capturing a thought is always the same keystroke.
5. **Warm and tactile.** Rounded cards, warm neutrals, springy hover/press, a palette that pops in. We move off Material's cool enterprise defaults but keep MudBlazor where it accelerates.

**The three locked decisions, restated as engineering constraints**

- **C1 — Foundation stays Blazor Interactive Server.** The graph, EF model, services, permissions, and routes remain intact. Client-side JS libraries are used for exactly two latency-critical surfaces: the **rich editor** (now) and the **canvas** (later). Everything else is server-rendered interactive components. The `<ImportMap>` lets us load hand-authored ESM (`shortcuts`, `theme-interop`) with **no app-level build pipeline**; the editor is the single exception — a pre-built, vendored bundle consumed as a static asset (§6, *The editor bundle*). The one existing module (`ReconnectModal.razor.js`) loads via a static `<script type="module">` tag, so the dynamic `import()` + callback pattern the editor relies on is proven by a spike sequenced first, not assumed.
- **C2 — First release is "the spine":** adaptive workspace shell + `Cmd/Ctrl-K` command palette + the rich Node **page** (document-first editor, wiki-links, backlinks, collapsible properties panel, inline title). Canvas, multi-views, spatial graph, and AI are explicitly later.
- **C3 — Aesthetic is "playful & tactile" (Capacities/Arc energy):** color-coded, iconographic object types; rounded cards; lively micro-interactions. Keep MudBlazor (themed) for dense controls, dialogs, snackbars; go custom for the shell, palette, cards, badges, and page chrome.

---

## 4. New information architecture

### The shell — four regions + two overlays

`WorkspaceShell.razor` replaces `MainLayout.razor` as the routed layout (update `Routes.razor` `DefaultLayout`). It keeps the four MudBlazor **providers** (`MudThemeProvider`, `MudPopoverProvider`, `MudDialogProvider`, `MudSnackbarProvider`) — infrastructure — and **drops** `MudLayout/MudAppBar/MudDrawer/MudMainContent/MudContainer` in favor of a CSS-grid shell we fully control.

```
Region A  Global Rail (56px)     — the few fixed escape hatches (icon-only)
Region B  Workspace Sidebar(260) — emergent nav: Favorites/Pinned/Collections/Recents
Region C  Top Bar (48px)         — back/forward, breadcrumbs, context actions, ⌘K, theme
Region D  Content Outlet (@Body) — routed pages; shell adds only max-width + padding
Region E  Properties/Backlinks   — right, collapsible, only on a Node page
Overlays  Command Palette (⌘K)   +  future Canvas host
```

Region B is where navigation *emerges*: `＋ New` / `⚡ Capture` actions, **Favorites** (`GetFavoritesAsync`), **Pinned** (`GetPinnedAsync`), a **Collections** tree (`GetCollectionsAsync` → `CollectionSummary(Node, Kind, IsOrdered, MemberCount)`; group by `.Kind`), **Recents** (§8), and a slim "Jump" strip to the fixed lenses. Dragging a node onto a collection calls `ICollectionService.AddMemberAsync`.

### The 8 destinations mapped to the *real* model

| Destination | Backing (real model) | Service / route | Status |
|---|---|---|---|
| **Home** | Aggregate dashboard | reuse `Today.razor`: `GetInboxAsync`, `GetRecentlyUpdatedAsync`, `GetPinnedAsync`, `IActionService`, `IEventService` | INTACT (restyle) |
| **Workspace** (sidebar landing) | `Node.IsFavorite` + `Node.IsPinned` + Collections + Recents | `GetFavoritesAsync` / `GetPinnedAsync` / `GetCollectionsAsync` / Recents (§8) | INTACT services + ADDITIVE recents |
| **Pages** | Record-kind `Node`s, document-first | `QueryAsync(new NodeFilter{ KindsIn = NodeFilter.RecordKinds })`; body = `Node.Body` | INTACT — **a lens, not a kind**. No `Page` NodeKind; `NodeKind.Journal` already covers daily notes |
| **Collections** | `NodeKind.Collection` + `Collection` profile | `/collections`, `ICollectionService` (`CollectionKind` Folder/List/Queue/Plain) | INTACT |
| **Graph** | Typed relations | `RelationService.GetConnectionsAsync`; reuse `/nookryptex` (`CryptexService.GetDatasetAsync`) | INTACT |
| **Search** | `NodeFilter.SearchText` (Title/Body/Url/tags) | `QueryAsync`; fronted by the palette; `/search` kept as deep-link | INTACT (+ `Take` cap, §8) |
| **Settings** | ASP.NET Identity account | existing `/Account/*` | INTACT + ADDITIVE `UserPreference` |
| **Canvas** | — none — | no NodeKind, no coordinate store | **NEW/ADDITIVE, deferred** — rail entry is a visible stub in the spine |

The discipline here: **Pages and every future View are lenses over the existing model, not new kinds or new queries.** The only genuinely new destination is **Canvas**, and it is stubbed, not built, in the spine.

---

## 5. Visual design system — "playful & tactile"

Two synchronized layers: a **CSS custom-property token layer** (`wwwroot/css/nook-tokens.css`, the source of truth) that the custom spine components read directly, and a **C# `NookTheme` (`MudTheme`)** that mirrors ~15 palette values so themed MudBlazor controls match. Keep them in sync by treating the CSS file as canonical and copying the palette hexes into `NookTheme`.

**Object-type color + icon (the heart of it).** `NodeUi.Icon(NodeKind)` already returns a Material glyph for all 16 kinds. We add **ADDITIVE** `NodeUi.KindAccent(NodeKind)` (hex) and `NodeUi.KindAccentVar(NodeKind)` (`--kind-*` name). The map (base hexes; chips derive tint/border/fg via `color-mix()`):

| Kind | Hex | Kind | Hex | Kind | Hex | Kind | Hex |
|---|---|---|---|---|---|---|---|
| Unclassified | `#8C8578` | Reference | `#5878A6` | Person | `#F76F5A` | Topic | `#17B0C4` |
| Note | `#4C7DF0` | Bookmark | `#F2416A` | Project | `#7C5CFF` | Resource | `#7E9C24` |
| Journal | `#E0863C` | List | `#23A968` | Place | `#A15C34` | Collection | `#B78430` |
| Observation | `#14B8A6` | Idea | `#F5B417` | Organization | `#A24C8C` | Event | `#EE5D9A` |

The eight highest-frequency kinds (Note, Idea, Person, Project, Journal, Bookmark, List, Topic) are maximally hue-separated. Icons can later swap to a rounder set (e.g. Phosphor) behind `ObjectTypeBadge` without touching call sites.

**Neutrals — warm "Sand" (never flat grey).** Hue ≈ 40, very low chroma. Light: canvas `#F7F5F0`, cards `#FFFFFF` (so they *lift*), text `#262119`, borders `#E9E4D9`/`#DED8CB`. Dark: canvas `#16130E` (warm near-black), surface `#201C15`, raised `#2A251C`, text `#F3EEE4`. Shadows are brown-tinted (`rgba(38,33,24,…)`), not pure black.

**Brand + semantic.** Primary **Iris `#5B54E8`** (creative, not corporate-blue; reserved — no NodeKind uses it; dark links lighten to `#8B84F5`). Accent **Tangerine `#F98A3C`** (capture, "new"). Semantics: success `#1E9E6A`, warning `#E0952A`, danger `#E24C4C`, info `#3E8FCB`, mapped from `ActionStatus`/`NodeState`.

**Typography** — self-host three variable `woff2` in `wwwroot/fonts/` and **delete** the Google Fonts Roboto `<link>` + `preconnect` from `App.razor`: **Bricolage Grotesque** (display/H1–H2, characterful), **Figtree** (body/UI/editor, warm workhorse), **JetBrains Mono** (ids, code, `NodeId`s). Root 16px; body 15px/1.6; editor reading text 17px/1.7.

**Spacing / radius / motion.** 4px grid. Radii: buttons/inputs 12, cards 16, panels/palette 20, chips pill — feed `--radius-md: 12px` into `NookTheme.LayoutProperties.DefaultBorderRadius`. Motion durations 80–360ms; easings `out`, `emphasized`, and a playful **spring `cubic-bezier(.34,1.56,.64,1)`**. Recipes: hover-lift `translateY(-2px)` + shadow bump (140ms); press `scale(.97)`; palette open fade + `scale(.96→1)` (200ms); drag (later) `scale(1.03) rotate(-1deg)`. Honor `prefers-reduced-motion` by collapsing transform durations to ~1ms and dropping the spring overshoot — encoded once in the token layer.

Dark mode stays in lockstep: a tiny `theme-interop` ESM writes `data-theme="dark|light"` on `<html>` whenever the theme flag flips, so the token layer and `MudThemeProvider.IsDarkMode` never desync.

---

## 6. Reusable layout components & component hierarchy

### Component tree (the spine)

```
WorkspaceShell.razor                    (@layout — replaces MainLayout; Routes.razor DefaultLayout)
├─ Mud{Theme,Popover,Dialog,Snackbar}Provider   (kept; Theme = NookTheme)
├─ CascadingValue<WorkspaceState>       (scoped service, §8)
├─ GlobalRail.razor                     Home / Search / Pages / Canvas(stub) / Settings / avatar
├─ WorkspaceSidebar.razor               Favorites · Pinned · Collections tree · Recents · Jump
│   └─ SidebarNodeLink.razor            (ObjectTypeBadge + title; drag→AddMemberAsync)
├─ TopBar.razor                         back/fwd · Breadcrumbs.razor · context actions · ⌘K · theme
├─ CommandPalette.razor                 (overlay, teleported to shell root)
├─ shortcuts.razor.js                   (one document keydown → DotNetObjectReference on shell)
└─ <main> @Body </main>

NodePage.razor  (@page "/nodes/{Id:int}" — replaces NodeDetail.razor)
├─ NodeHeader.razor        static <h1> focus target wrapping InlineTitleEditor + ObjectTypeBadge + StateChip + SaveIndicator + ⋯ menu
├─ EditorHost.razor  ──►  EditorHost.razor.js   (browser-owned editor; SignalR only on debounced save)
├─ PropertiesPanel.razor   [collapsible right rail]
│   ├─ Kind chips → PromoteAsync · State → SetStateAsync · pin/favorite · dates
│   ├─ TagAutocomplete + TagChips        (reused)
│   ├─ CollectionAssignmentPanel         (reused, ShowHeader=false)
│   ├─ ConnectionsPanel                  (reused — explicit typed relations)
│   └─ ActionsPanel                      (reused — full task list)
├─ BacklinksPanel.razor    GetConnectionsAsync().Backlinks, grouped by Connection.Label
└─ ActivityStrip.razor     ActivityService.GetForNodeAsync + EventService.GetEventsForNodeAsync
```

### The EditorHost JS-interop pattern (the load-bearing decision)

**A rich editor over Blazor Server is only viable if keystrokes never round-trip.** We enforce this *structurally*: the editor's content is owned by a browser ES module and is **never** bound to a Blazor `@bind`/`@oninput`. `EditorHost.razor` renders one `<div @ref>` and, after first interactive render, becomes inert (`ShouldRender() => false`).

- **Boot** (`OnAfterRenderAsync(firstRender)`, guarded so it never runs under static SSR): `_module = await JS.InvokeAsync<IJSObjectReference>("import", "./Components/.../EditorHost.razor.js")`; create a `DotNetObjectReference<EditorHost>`; call `initialize(elementRef, dotNetRef, initialMarkdown, { debounceMs: 800, linkIndex })`. This dynamic `import()` + `DotNetObjectReference` round-trip is standard Blazor interop but is **not** yet used in this repo (the one existing module loads via a static tag), so Phase 1(a) proves path-resolution + disposal on Server with a throwaway spike before the editor depends on it. No bundler is needed for the loader module itself.
- **Client owns state.** Every keystroke mutates the DOM in the browser only. The module debounces (~800 ms idle) and hard-flushes on blur / `Cmd-S` / `visibilitychange` / `beforeunload`, then calls back **one** `[JSInvokable]` with the whole snapshot.
- **`[JSInvokable]` callbacks:** `SaveBodyAsync(markdown)`, `SearchNodesAsync(query)` (for `[[` popup, backed by `QueryAsync`), `ResolveOrCreateLinkAsync(title)`, `ToggleActionAsync(id, done)`, `CreateActionAsync(text)`. These are the *only* server messages during editing.
- **Teardown** (`IAsyncDisposable`): `flush()` (last-chance save) → `dispose()`, all wrapped to swallow `JSDisconnectedException` (mandatory on Server). A periodic snapshot is the safety net for hard tab-close.

**The editor library:** locked decision C1 explicitly sanctions a client-side JS library for the rich editor, so behind this boundary we ship **TipTap (ProseMirror)** — its **node views** + **`@tiptap/suggestion`** are the most mature primitives for fusing blocks to server entities (a checklist line that *is* an `ActionItem`, a `[[link]]` that *is* a `NodeRelation`), which is exactly the high-value, high-risk part. TipTap is delivered as **one vendored ESM bundle** produced by a single isolated `esbuild` step scoped to `/editor-src` (the app-wide pipeline stays absent). Markdown fidelity is guaranteed by constraining the schema to round-trippable nodes and a `parse(serialize(doc)) === doc` snapshot test; Milkdown (remark-native) is the fallback if fidelity fights back. This bundler step is sequenced **first** in Phase 1 to de-risk it, and the `EditorHost` boundary means the bundle can be swapped without touching any Blazor code.

**The editor bundle — resolving the "no build pipeline" tension.** C1's "no build pipeline" holds at the *application* level and must stay that way; TipTap/ProseMirror simply cannot be hand-authored ESM and must be bundled once. We make that explicit instead of waving it away:

- **Delivery is a committed, pre-built, vendored ESM bundle** at `wwwroot/lib/editor/nook-editor.<hash>.js`, referenced through the existing `<ImportMap>`. The `dotnet` build and CI need **no Node** — they consume a static asset like any other, so the app stays build-free and offline-friendly.
- **Producing** the bundle is an isolated, opt-in step under `/editor-src`: a pinned Node version (`.nvmrc`, 20 LTS) + a pinned `esbuild`, one `npm run build` → the committed output above. It runs only when a developer intentionally bumps the editor, never in the app's release path.
- **Versioning / integrity:** the output filename is content-hashed and the import-map entry pins that exact file; upgrading is one file + one line, behind the `EditorHost` boundary.
- **Tradeoff, stated:** we accept a checked-in generated artifact (hash-named, reviewable) in exchange for keeping the app itself free of npm/vite. A full app-level JS pipeline is explicitly rejected for the spine. Fallback if bundle diffs are unacceptable: Milkdown, delivered the same committed-bundle way.

**Phase boundary (to remove ambiguity).** The spine ships *functional* `[[link]]` resolution (→ the seeded `mentions` relation), a live backlinks surface, and inline task **checkboxes** you can toggle — all with plain rendering. The *rich, interactive* node-views (embedded child pages, slash menu, tables, callouts) are **Phase 2**. The `[JSInvokable]` seams above (`SearchNodesAsync`, `ResolveOrCreateLinkAsync`, `ToggleActionAsync`, `CreateActionAsync`) are Phase 1; they simply render plainly until Phase 2 upgrades the views.

### Existing components — reused / wrapped / retired

| Component | Disposition | Note |
|---|---|---|
| `MainLayout.razor` | **wrap** | Keep the 4 providers + dark-mode logic; move into `WorkspaceShell`; drop the Mud layout scaffold |
| `NavMenu.razor` | **retire** | Replaced by `WorkspaceSidebar`; salvage its `LocationChanged` tag-refresh pattern |
| App-bar search + `OnSearchKeyUp` | **retire** | Folded into the palette; `/search` kept as a deep-link results page |
| `NodeDetail.razor` (3-row grid) | **retire** | Superseded by `NodePage`; its `LoadAsync`/`RefreshCounts` call patterns are lifted into the new sub-components |
| `NodeEditor.razor` (form) | **wrap** | Retired *from the page* (document editing replaces it); **kept** for `/capture` + new-node modal |
| `ConnectionsPanel`, `ActionsPanel`, `CollectionAssignmentPanel`, `TagAutocomplete`, `TagChips`, `NodeAutocomplete`, `EventPanel` | **reuse** | Dropped into `PropertiesPanel` / `NodePage` unchanged |
| `NodeCard`, `NodeListView` | **reuse** | List pages keep them under the new shell; restyled via tokens |
| `Today.razor` | **reuse** | Becomes Home; logic unchanged, restyled with `ObjectTypeBadge` + tokens |
| `Nookryptex.razor` + `nookryptex.js` | **reuse** | Serves as the Graph destination; its ESM pattern is the template for `shortcuts.razor.js` |
| `NodeUi.cs` | **reuse** | Keep `Icon`/`StateColor`/`AssignableKinds`; **ADDITIVE** `KindAccent`/`KindAccentVar` |
| `INode/IRelation/ICollection/ITag/IAction/IEvent Service` | **reuse** | Every destination binds to existing methods; no breaking changes |

---

## 7. Wireframes

### (a) Workspace Shell + Home

```
┌──────┬────────────────────────────┬─────────────────────────────────────────────────┐
│ RAIL │  WORKSPACE  (260px)        │  ‹ ›   Home                     ⌘K Search   ☾  👤 │
│ 56px ├────────────────────────────┼─────────────────────────────────────────────────┤
│  ⌂   │  ＋ New      ⚡ Capture      │   Good evening, Cap                               │
│  ⌕   │                            │                                                   │
│  ▦   │  ★ FAVORITES               │   INBOX · 5 to triage                       ▸ all │
│  ◫•  │   ● Jamie        Person    │   ┌───────────┐┌───────────┐┌───────────┐         │
│  ⚙   │   ● Roadmap      Note      │   │● note     ││● idea     ││● bookmark │  …      │
│      │  📌 PINNED                 │   └───────────┘└───────────┘└───────────┘         │
│  👤  │   ● Q3 Plan      Project   │                                                   │
│      │  ▾ COLLECTIONS             │   PINNED                                          │
│      │    📁 Work          (12)   │   ┌─▌──────────┐┌─▌──────────┐                     │
│      │    📋 Reading queue  (8)   │   │▌Q3 Plan    ││▌Launch prep │  …                 │
│      │  🕘 RECENTS                │   └────────────┘└────────────┘                     │
│      │   ● Launch prep  Project   │                                                   │
│      │  ── Jump ──                │   RECENTLY UPDATED                                │
│      │  Pages · Graph · Canvas◦   │   ● Meeting notes   ● Field guide   ● Retro   …    │
└──────┴────────────────────────────┴─────────────────────────────────────────────────┘
  ●=ObjectTypeBadge (kind color-dot + icon)   ▌=kind-accent card border   ◦=stub   ☾=theme
```

### (b) Node Page (content + collapsible properties + backlinks)

```
┌───────────────────────────────────────────────────────────┬──────────────────┐
│ ‹ ›  💡 Idea ▸ Projects ▸ "Rebuild the spine"   ⌘K  ⋯  ☆   │  PROPERTIES   ⌄  │ ← [ ] toggles
├───────────────────────────────────────────────────────────┤  ● Kind   Idea ▾ │
│  💡  Rebuild the spine                     [ Active ▾ ]     │  ○ State Active ▾│
│      (inline H1 · contenteditable)          Saved ✓        │  ☆ Fav   ⌖ Pin   │
│  Idea · edited 2m ago                                       │  # spine systems │
│  ┌─ AI: summary ─────────────────────── (reserved) ─┐      │  ▸ Collections   │
│  └────────────────────────────────────────────────┘       │  ▸ Relationships │
│                                                            │  ▸ Actions   (2) │
│  Type “/” for blocks · “[[” to link                        │  ─────────────── │
│  A document-first editor whose  [[Knowledge Graph]]  links │  AI ▸ links  ◦   │
│  resolve to real nodes.                                    │  AI ▸ related ◦  │
│  ☑ read services      ☐ ship the spine   ← ActionItems     │                  │
│  ▤ child page: “Migration plan”          ← contains        │                  │
│  ┌ callout ─────────────────────────────────────────┐     │                  │
│  │ > [!note] keystrokes never touch SignalR          │     │                  │
│  └───────────────────────────────────────────────────┘    │                  │
│  ── BACKLINKS ─────────────────────────────────────        │                  │
│  ← mentioned by · Project “Nook v2”                        │                  │
│  ── ACTIVITY ──────────────────────────────────────        │                  │
│  created · promoted→Idea · 3 events reference this ▸        │                  │
└───────────────────────────────────────────────────────────┴──────────────────┘
  Content column = title + body + backlinks + activity ONLY. Every knob lives in the rail.
```

### (c) Command Palette (`Cmd/Ctrl-K`)

```
                 ┌────────────────────────────────────────────────────┐
                 │  🔍  quarterly plan▌                           ⌘K   │  sigils: >cmd /go #tag @entity
                 ├────────────────────────────────────────────────────┤
                 │  ACTIONS                                           │
                 │  ⚡ Quick-capture “quarterly plan” → Inbox      ↵   │  QuickCaptureAsync
                 │  📁 Create Project “quarterly plan”           ⌘↵  │  CreateAsync(Kind=Project)
                 │  📝 Create Note “quarterly plan”                   │  (kinds = AssignableKinds)
                 │  NODES                                            │
                 │  ● Q3 Quarterly Plan            Project · 2d       │  instant title index (JS)
                 │  ● Planning notes               Note · updated     │
                 │  — full matches ————————————————————— ⟳          │  QueryAsync (debounced 250ms)
                 │  RECENTS                                          │
                 │  🕘 Jamie Rivera                 Person            │  WorkspaceState.Recents
                 │  GO TO                                            │
                 │  → Home   → Collections   → Graph   → Analytics    │  CommandRegistry
                 └────────────────────────────────────────────────────┘
                    ↑↓/jk move   ↵ open   ⌘↵ create   esc close
```

---

## 8. Backend impact — "what stays intact"

The proof that the Node architecture is preserved is that nearly every spine feature binds to a method that already exists. New backend is small, additive, and isolated behind interfaces.

| Touch-point | Mapping | Status |
|---|---|---|
| Node page load | `NodeService.GetByIdAsync` → `Node` (Title/Body/Kind/State/IsPinned/IsFavorite/dates) | **INTACT** |
| Rich Markdown body | `Node.Body` (`nvarchar(max)`, already `string?`) reinterpreted as GFM Markdown | **INTACT** (convention only, no schema) |
| Inline title / kind / state / pin / fav / archive | `UpdateAsync`, `PromoteAsync`, `SetStateAsync`, `TogglePinAsync`, `ToggleFavoriteAsync`, `ArchiveAsync`, `RestoreAsync` | **INTACT** |
| Autosave body (debounced) | **ADDITIVE** `INodeService.SaveBodyAsync(int id, string? body)` — writes Body + UpdatedAt only, so per-tick saves don't reload/clobber tags via `UpdateAsync(node, tagIds)` | **ADDITIVE** (composes from existing) |
| `[[wikilink]]` search | `QueryAsync(new NodeFilter{ SearchText })` via `[JSInvokable] SearchNodesAsync` | **INTACT** |
| `[[wikilink]]` → relation | `RelationService.AddRelationAsync(id, targetId, mentionsTypeId)` where **`mentions` is already seeded** (`GraphSeedData.RelationTypes`, `"mentions"/"mentioned by"`, `Category=Reference`) | **INTACT — no seed needed** |
| Reconcile links on save | **ADDITIVE** `IWikiLinkService` — diffs `[[chips]]` vs existing `mentions` outgoing via `GetConnectionsAsync` + `AddRelationAsync`/`RemoveRelationAsync` (no new table) | **ADDITIVE (logic)** |
| Backlinks panel | `GetConnectionsAsync(id).Backlinks` (`Connection.OtherNodeId/OtherTitle/OtherKind/Label/Note`), grouped by `Label` | **INTACT** |
| Inline checklist ↔ actions | `IActionService.GetForNodeAsync`; create standalone document checkboxes as `CreateAsync(new ActionItem{ Kind=Task, TargetNodeId=id })` — **`Task`, not `ChecklistItem`** (the latter is a *child* made via `AddChecklistItemAsync(parentActionId,title)` and is hidden from top-level lists by `ActionFilter.ExcludeChecklistItems=true`); toggle via `CompleteAsync`/`ReopenAsync` | **INTACT** |
| Child pages | `QuickCaptureAsync` + `AddRelationAsync(parent, child, containsTypeId)` (`GraphSeedData.ContainsRelationTypeName = "contains"`); list via `GetConnectionsAsync().Outgoing` filtered to the contains label | **INTACT** |
| Properties: tags / collections / relations / actions | reuse `TagAutocomplete`+`ITagService`, `CollectionAssignmentPanel`+`ICollectionService.GetCollectionSummariesForNodeAsync`, `ConnectionsPanel`, `ActionsPanel` | **INTACT** |
| Activity + referencing events | `ActivityService.GetForNodeAsync(userId,id,take)`; `EventService.GetEventsForNodeAsync(id)` | **INTACT** |
| Sidebar Favorites / Pinned / Collections | `GetFavoritesAsync`, `GetPinnedAsync`, `GetCollectionsAsync` → `CollectionSummary` | **INTACT** |
| Palette search + result cap | `QueryAsync(NodeFilter{ SearchText })` + **ADDITIVE** `int? Take` on `NodeFilter` (`LIKE` is uncapped today — two-line fix, needed for a live palette) | INTACT + **ADDITIVE** |
| Palette create / navigate / theme | `CreateAsync`/`QuickCaptureAsync`/`ICollectionService.CreateAsync`, `NavigationManager`, theme flag; **ADDITIVE** in-memory `CommandRegistry`; admin migration row **server-side gated** before show *and* execute | INTACT + **ADDITIVE** |
| **User preferences + Recents + last-opened** | **NEW** `UserPreference` (1 row/user: `Theme`, `SidebarCollapsed`, `LastOpenedNodeId` (denormalized, no FK), `RecentNodeIds`/`PreferencesJson`, `UpdatedAt`) + `IUserPreferenceService`; runtime MRU held in scoped `WorkspaceState`, cold-start fallback `GetRecentlyUpdatedAsync` | **NEW** (one migration) |
| Per-kind color | **ADDITIVE** `NodeUi.KindAccent`/`KindAccentVar` + `--kind-*` tokens (companion to existing `Icon`) | **ADDITIVE** |
| Theme state | **ADDITIVE** scoped `ThemeState` (lift `MainLayout._isDarkMode` out; persist to `UserPreference`); `MudThemeProvider.IsDarkMode` binds to it; `theme-interop` writes `data-theme` | **ADDITIVE** |
| Global shortcuts | **NEW** `shortcuts.razor.js` (collocated ESM) → `DotNetObjectReference` on `WorkspaceShell`; suppress single-letter chords in text inputs | **NEW (interop)** |
| Multi-view sort/group/paging (later) | **ADDITIVE** optional `NodeSortBy? SortBy`, `bool SortDescending`, `int? Skip/Take` on `NodeFilter` (`Take` null-defaulted → non-breaking) | **ADDITIVE** |
| Attachments / images (later) | **NEW** `Attachment` (metadata) + `IAttachmentStore` (disk now, blob later) + authorized `/attachments/{id}` endpoint; bytes never in SQL; Markdown stores the URL | **NEW** |
| Canvas (later) | **ADDITIVE** `NodeKind.Canvas` (string-stored, safe to append) + **NEW** `CanvasBoard` (1:1 profile, `SceneJson`, mirrors `Collection`/`EventDetails`) + **NEW** `CanvasObjectRef` (object→Node links only; edges stay visual in JSON) | **ADDITIVE + NEW** |
| AI (later) | **NEW** `NodeEmbedding` + `NodeAiArtifact` sidecars keyed by `NodeId`; **NEW** `ISearchService` seam whose v1 wraps `NodeFilter.SearchText`. `INodeService`/`IRelationService` never modified | **NEW** |

**House-style compliance:** every new user/node FK uses `DeleteBehavior.Restrict` (or `NoAction` on one leg) to avoid the multiple-cascade-path errors the auth-gotchas memo warns about; `Cascade` only for 1:1 profiles (`CanvasBoard`). New enum members are string-stored, so appending is migration-safe. All new services register `AddScoped` beside the existing dozen-plus in `Program.cs`, resolved through `IDbContextFactory<NookContext>`.

**Migration footprint for the spine = one migration** (the `UserPreference` table). `mentions` is already seeded; the Markdown body needs no schema change. `Attachment` lands with editor images; Canvas/AI tables are deferred and independent.

---

## 9. Implementation roadmap (ordered by value & risk)

| Phase | Value | Risk | Key work items |
|---|---|---|---|
| **1 — The Spine** *(this release)* | Transforms the product: adaptive shell, `⌘K` everywhere, a real document-first page. Highest user-visible payoff. | **Med-High** — new shell touches every page's DOM assumptions; editor interop is the crux. | (a) Isolated `esbuild` step → vendored TipTap ESM bundle; stand up `EditorHost.razor` + `.razor.js` (client-owned state, 800ms debounce, `ShouldRender=>false`, `flush()`-on-dispose) — **do first to de-risk**. (b) `nook-tokens.css` + `NookTheme` + self-hosted fonts (drop Roboto CDN) + `NodeUi.KindAccent` + `ObjectTypeBadge`. (c) `WorkspaceShell` (+ `GlobalRail`, `WorkspaceSidebar`, `TopBar`, `Breadcrumbs`), `WorkspaceState`, `Routes.razor` `DefaultLayout` swap, and give every page a **non-editable** `<h1>` focus target — on `NodePage`, `NodeHeader` wraps a static `<h1>` distinct from the contenteditable title so `FocusOnNavigate Selector="h1"` never drops the cursor into the editor (or scope the selector). (d) `CommandPalette` + `CommandRegistry` + `shortcuts.razor.js` + `NodeFilter.Take` cap. (e) `NodePage` (InlineTitle, `PropertiesPanel` wrapping reused panels, `BacklinksPanel`, `ActivityStrip`); `IWikiLinkService`; `SaveBodyAsync`. (f) `UserPreference` + `IUserPreferenceService` + `ThemeState` (one migration). |
| **2 — Rich editor & Pages** | Turns the editor from good to delightful: slash menu, tables, callouts, images, child-page embeds, templates. | **Med** — Markdown round-trip fidelity for custom nodes; the one new storage subsystem. | Enrich the editor with the *rich interactive* rendering — slash menu, tables, callouts, and embedded `wikiLink`/`childEmbed`/task node-views (Phase 1 already ships functional `[[link]]`→`mentions`, backlinks, and inline task toggling with plain rendering; Phase 2 upgrades those blocks to interactive/embedded); `parse(serialize)==doc` snapshot test; **NEW** `Attachment` + `IAttachmentStore` + `IAttachmentService` + authorized endpoint; Pages lens (`RecordKinds`) with kind templates; `AiSlot` placeholders reserved. |
| **3 — Multi-Views** | Same nodes, many lenses (Table, Gallery, Kanban, Calendar, Timeline) — big organizational value, low model risk. | **Low-Med** — Calendar/Kanban must plot the *right* dates or look empty. | **ADDITIVE** `NodeFilter` sort/group/paging; view components over `QueryAsync`; Kanban drag → `SetStateAsync`/`PromoteAsync`/`CompleteAsync`; Calendar/Timeline draw dates from `EventDetails.OccurredAt` + `ActionItem.DueDate` (reuse `EventService.GetTimelineAsync`, `ITimelineService.BuildAsync`) — UI must state what it plots. |
| **4 — Canvas** | The second latency-critical surface; spatial thinking. | **High** — genuinely new persistence + JS-interop surface. | **ADDITIVE** `NodeKind.Canvas`; **NEW** `CanvasBoard` (`SceneJson`, JS-owned) + `CanvasObjectRef`; `ICanvasService` with a single atomic write path (JSON + refs); canvas host reuses the `EditorHost` interop discipline. Edges stay visual — the semantic graph is never mutated by drawing. |
| **5 — Spatial Graph polish** | Elevates `/nookryptex` into a first-class graph explorer. | **Low** — data already exists. | Reuse `RelationService.GetConnectionsAsync` + `CryptexService.GetDatasetAsync`; richer layout/filtering; deep-link from the palette and backlinks. |
| **6 — AI** | Summaries, suggested links, semantic search, related notes. | **Med-High** — vector infra is a deferred infra decision, not a code decision. | **NEW** `NodeEmbedding`/`NodeAiArtifact` sidecars + `IEmbeddingService`; `ISearchService` swaps `LIKE` for vector search behind the unchanged palette; `IAiService` fills the reserved `AiSlot`s. `GetRelatedByTagsAsync` seeds a non-AI "related" fallback in the interim. |

**Sequencing rationale:** Phase 1 front-loads the single highest-risk item (the editor interop bundle) so it is proven before the shell and palette depend on it, and it ships the entire user-visible transformation with exactly one migration. Every later phase is *additive by construction* — it attaches to the existing graph via new tables, new enum members, or new service seams, and never edits `INodeService`/`IRelationService` or the `Node`/`NodeRelation` schema. That is the guarantee: **the second brain is a new surface on an unchanged graph.**

---

## 10. Resolved review issues (adversarial pass)

This spec was reviewed by an adversarial critic tasked with finding invented APIs, hidden backend breaks, and Phase-1 deliverability gaps. It verified claims against the real code. Verdict: **sound, with fixes** — all folded in above:

1. **Editor toolchain (was under-specified).** TipTap/ProseMirror needs bundling, which sat in tension with "no build pipeline." Resolved in §6 *The editor bundle*: a committed, hash-named, vendored ESM bundle consumed via `<ImportMap>` (app build + CI need no Node); producing the bundle is an isolated, pinned, opt-in `/editor-src` step. This is the single largest Phase-1 risk and is now sequenced first.
2. **Interop precedent corrected.** `ReconnectModal.razor.js` loads via a *static* module tag, not dynamic `import()` + `DotNetObjectReference`. The editor's pattern is standard Blazor but unproven here, so Phase 1(a) includes a throwaway interop **spike** (path resolution + disposal on Server) before the editor depends on it.
3. **Phase boundary made explicit.** The spine ships *functional* `[[link]]`→`mentions`, backlinks, and inline task toggling with plain rendering; *rich interactive* node-views (slash menu, tables, callouts, embeds) are Phase 2.
4. **Checklist mapping fixed.** Standalone document checkboxes are `ActionItem.Kind=Task` (node-attached, shown in default lists), not `ChecklistItem` (a child of a parent action, hidden by `ActionFilter.ExcludeChecklistItems`).
5. **`CollectionSummary` field name** corrected to `.Kind` (type `CollectionKind`), with `.IsOrdered` noted.
6. **Focus hijack avoided.** `NodePage` gives `FocusOnNavigate Selector="h1"` a non-editable `<h1>` target so navigation never drops the cursor into the contenteditable title.