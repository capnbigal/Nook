# Nook Knowledge-Graph — Implementation Report

> Delivered: 2026-07-02. This report documents the end-to-end implementation of the core knowledge-graph redesign (Phase 0 + Phase 1 + Phase 2) as one cohesive delivery. Companion design docs: `docs/Knowledge-Graph-Redesign.md`, `docs/Knowledge-Graph-Migration-Plan.md`.

## 1. What Was Implemented

The single overloaded `Item` model has been superseded by a hybrid **Node-centered graph** on the existing .NET 10 / Blazor Interactive Server / MudBlazor / EF Core / SQL Server / ASP.NET Core Identity stack. Everything addressable is a `Node`; behaviour comes from related tables (relations, collections, actions, events, tags), never from the node's kind. All of the locked functionality is implemented and working:

- **Quick capture** → an `Unclassified` node in the `Inbox` state (title-only is valid).
- **Progressive organisation** → promote a node's kind in place, preserving its identity and all attached data.
- **People / Projects / Places** and every other kind via one reusable list/detail/editor.
- **Typed relations + backlinks** with a seeded system vocabulary, symmetric canonicalisation, self-link/duplicate/cross-user guards.
- **Node-backed collections** (Folder/List/Queue/Plain) with ordered many-to-many membership and move up/down.
- **Actions** (Task/Reminder/ChecklistItem) attachable to any node, with **ActionContext** multi-role rollups and checklists.
- **Reusable node → repeated dated actions**; completing an action never changes its node.
- **Node-backed events** (verb/subject/object/place/participants) with fast free-text capture and a lazy "self" Person.
- **Today / Daily Planning**, Inbox, All, Unassigned, Search, Tags, Archive, Timeline, Analytics — all retargeted to the graph.
- **Legacy Item pages** are now navigation-only redirects; they never read or write legacy data.
- **Explicit, idempotent migration/backfill** with a parity-validation report, behind an admin page.

Auth, per-user isolation, the existing visual style, dark mode, and the `IDbContextFactory` short-lived-context pattern are all preserved.

## 2. New Models, Migration, Services, Routes, UI

### Models (`Models/`)
- `GraphEnums.cs` — `NodeKind`, `NodeState`, `RelationCategory`, `ActionKind`, `ActionStatus`, `ActionPriority`, `ActionVerb`, `ActionContextRole`, `CollectionKind`, `EventParticipantRole`.
- `Node`, `RelationType`, `NodeRelation`, `NodeTag`, `Collection`, `CollectionMembership`, `ActionItem`, `ActionContext`, `Verb`, `EventDetails`, `EventParticipant`, `MigrationAudit`.
- Extended: `Tag` (+`NodeTags`), `ApplicationUser` (+ nullable `SelfNodeId`), `ActivityLog` (+ nullable `NodeId`).

> **Naming:** the product term is **Action**; the C# entity is **`ActionItem`** to avoid ambiguity with `System.Action`.

### Data (`Data/`)
- `NookContext` — DbSets + full graph configuration (`ConfigureGraph`): `DeleteBehavior.Restrict` on all user FKs and both Node FKs of `NodeRelation`/`CollectionMembership`; **filtered unique indexes** for nullable `UserId` on `RelationType` and `Verb`; unique constraints on relations, `NodeTag`, `CollectionMembership`, `ActionContext`; 1:1 `Collection`/`EventDetails`; hot-path indexes (node user/state/kind/updated, outgoing relations, backlinks, collection order, action rollups, reminders, per-node actions, event timelines). Timestamp stamping extended to `Node` and `ActionItem`.
- **Migration:** `Data/Migrations/20260702232156_AddKnowledgeGraph.cs` — **additive only** (creates the new tables; adds `AspNetUsers.SelfNodeId` and `ActivityLogs.NodeId`; does **not** drop or alter `Item`, `ItemLink`, or `ItemTag`).

### Services (`Services/`)
- `INodeService`/`NodeService`, `IRelationService`/`RelationService`, `ICollectionService`/`CollectionService`, `IActionService`/`ActionService`, `IEventService`/`EventService`.
- `IGraphMigrationService`/`GraphMigrationService` (+ `GraphSeedData`), `NodeFilter`, `RelationModels`, `NodeUi` (presentation helpers).
- Retargeted: `TagService` (node counts + node assign/remove), `AnalyticsService` (nodes + actions), `ActivityService` (+`LogNodeAsync`/`GetForNodeAsync`). Registered in `Program.cs`. `ItemService` retained only for compilation/legacy compatibility; no route writes to it.

### Routes / UI (`Components/`)
- **New pages:** `Today` (`/today`, `/dashboard`), `Capture` (`/capture`), `Inbox`, `AllNodes` (`/all`), `NotesRecords` (`/notes`), `People`, `Projects`, `Places`, `Unassigned`, `SearchPage` (`/search`), `NodeDetail` (`/nodes/{id}`), `Collections` + `CollectionDetail`, `Actions`, `Events`, `TagNodes` (`/tags/{id}`), `GraphMigration` (`/admin/graph-migration`).
- **Reusable components:** `NodeCard`, `NodeEditor`, `NodeListView`, `NodeAutocomplete`, `ConnectionsPanel`, `ActionsPanel`, `EventPanel`.
- **Retargeted:** `Tags`, `Archive`, `Timeline` (fixed the silent 500-row cap → explicit "showing most recent N" + Load more), `Analytics`, `Log`, `NavMenu`, `MainLayout` (search → `/search`, `+` → `/capture`), `Home` (→ `/today`).
- **Redirects:** `LegacyRedirects` (`/items`, `/items/new`, `/todos`, `/reminders`, `/bookmarks`) and `LegacyItemDetailRedirect` (`/items/{id}` → `/nodes/{id}`, valid because NodeId == ItemId).
- **Removed:** the old `Dashboard`, `Items`, `NewItem`, `ItemDetail`, `Todos`, `Reminders`, `Bookmarks` pages and the now-unused `ItemCard`/`ItemEditor`/`ItemFilterBar`/`DashboardSection` components.

## 3. Migration / Backfill Mechanism & Commands

**The backfill is never run automatically.** On startup `DbSeeder` only (a) applies schema migrations and (b) idempotently seeds system relation types + verbs — both non-destructive. The Item→Node conversion is explicit.

**Apply schema:**
```bash
# automatic on startup in every environment (DbSeeder → Database.MigrateAsync), or manually:
ASPNETCORE_ENVIRONMENT=Development dotnet ef database update
```

**Run + validate the backfill (explicit):** sign in and open **`/admin/graph-migration`**, then:
1. **Seed system data** (idempotent; also runs at startup).
2. **Run backfill** — Items→Nodes (IDENTITY_INSERT + reseed), ItemTag→NodeTag, ItemLink→NodeRelation, ParentItemId→`contains`, Todo/Reminder→Node+ActionItem, ActivityLog.NodeId backfill, MigrationAudit for skips.
3. **Validate** — parity report (counts, ID mapping, coverage, audit findings).

Programmatic equivalent (e.g. from a maintenance script): resolve `IGraphMigrationService` and call `SeedSystemDataAsync()` → `BackfillAsync()` → `ValidateAsync()`.

**Backfill rules (as implemented):** ItemType→NodeKind (Note/Idea/Reference/List direct; Thought→Note; Bookmark→Bookmark; Todo & Reminder→Note); `State` = Archived if `ArchivedAt` set else Active; Todo→Task action (status/priority/due/completed preserved); Reminder or any `ReminderDate`→Reminder action; a Todo with a `ReminderDate` yields **both**; a **stray `DueDate` on a non-actionable item is not turned into a Task** — it is recorded as a `SkippedDueDate` audit; unmapped `ItemLink.LinkType` → `related to` + `UnmappedLinkLabel` audit. Every step is idempotent (`NOT EXISTS`/marker guarded). IDs are preserved (`NodeId == ItemId`) and the Node identity is reseeded with `DBCC CHECKIDENT`.

## 4. Validation Checks

`GraphMigrationService.ValidateAsync()` returns a pass/fail report covering: Node≥Item count; every Item has a Node with a matching id; NodeTags cover migratable ItemTags; NodeRelations cover ItemLinks+parents; Task actions cover Todos; Reminder actions cover reminder sources; system relation types & verbs seeded; ActivityLog.NodeId backfilled where the node exists; plus a list of audit findings (`SkippedDueDate`, `UnmappedLinkLabel`).

## 5. Tests — Commands & Results

```bash
dotnet build Nook.sln          # succeeds, 0 warnings / 0 errors
dotnet test Nook.Tests/Nook.Tests.csproj
```

**Result: 48 passed, 0 failed, 0 skipped (15 s).**

- **InMemory service/logic tests (44):** node creation & progressive promotion (identity preserved); cross-user isolation across Node/Relation/Collection/Action/Event/Tag services; relation duplicate & self-link prevention; symmetric canonicalisation + inverse-label backlinks; collection membership, ordering, node-in-multiple-collections; action completion preserving node state; reusable node → multiple dated actions; ActionContext rollups; checklist grouping; event capture, participants & relationships; lazy self-Person reuse; tag retargeting; retargeted analytics.
- **Real SQL Server LocalDB integration tests (4)** via `[SqlServerFact]` (auto-skips with a clear reason when LocalDB is absent): full backfill migrates all legacy data **preserving IDs** and is **idempotent** (second run creates nothing; validation all-pass); **identity reseed** so new nodes don't collide; **filtered unique index** allows exactly one system relation type per name; cross-user isolation on real SQL Server.

**Skipped integration tests:** none — SQL Server LocalDB (`MSSQLLocalDB`) was available in this environment, so all four ran and passed. On a machine without LocalDB they report as *Skipped* with the exact reason (never a false pass).

**Runtime smoke test (fresh LocalDB):** the app boots, applies migrations, seeds graph demo data, serves `/` and `/login` (200), gates authed routes to `/login` (302), and — signed in as the demo user — renders `/today`, `/all`, `/nodes/1` (the seeded "Welcome to Nook" node with connections/actions panels), `/people`, `/collections`, `/actions`, `/events`, `/inbox`, `/unassigned`, `/timeline`, `/analytics`, and `/admin/graph-migration` with **zero server-side exceptions**.

## 6. Migration Audit Findings (categories)

The backfill records durable `MigrationAudit` rows and surfaces them in the validation report:
- **`SkippedDueDate`** — a non-Todo/Reminder legacy item carried a `DueDate`; per the locked rule no Task was invented. Re-add an action manually if desired.
- **`UnmappedLinkLabel`** — a legacy `ItemLink.LinkType` string didn't match a system relation type; it was migrated as `related to` (original label retained in the audit detail).

## 7. Manual Deployment / Cutover Steps

1. **Back up** the SQL Server database and verify it restores to a scratch DB.
2. Deploy the build. On first start it applies the additive migration and seeds system data automatically (no data conversion).
3. Sign in, open **`/admin/graph-migration`**, run **Seed → Backfill → Validate**; confirm all checks pass and review audit findings.
4. The graph model is now authoritative; legacy routes already redirect. Legacy `Item*` tables are untouched and remain a rollback snapshot.
5. (Later, separate change, after a soak period) drop `Item`, `ItemLink`, `ItemTag` — **not** part of this delivery.

No deployment credentials, secrets, domains, or hosting configuration were changed.

## 8. Backup & Rollback Guidance

Per the locked decision, there is **no lossy new→legacy projection**. Legacy tables are an immutable snapshot, not a live shadow:
- **Before cutover:** rollback = revert the deployment; the additive schema is inert and the app can run on the old build (legacy tables intact). Optionally restore the pre-deploy backup.
- **After cutover / once new nodes receive writes:** the new model is authoritative and new graph data (relations, collections, actions, events) has no legacy representation. Rollback therefore requires **restoring a verified database backup** (losing changes made after cutover) or a deliberate export/import — not a flag flip. Take the backup in step 1 accordingly.

## 9. Remaining (Intentionally Deferred) Phase 3 Items

Not built, by instruction: visual graph/canvas; graph database; EAV/property-bag; full-text-search infrastructure; email/push/SMS delivery; recurrence engine / ActionTemplate engine; PWA/offline; Markdown editor/rendering overhaul; external integrations; microservices/queues/background-worker infrastructure. Reminders are surfaced in-app (Today + Actions) only. Entity profile satellites (PersonProfile/etc.) were left for when a real field is needed.

## 10. Decisions Made Without User Input (using the locked defaults)

- Legacy **Thought** items → `Node.Kind = Note` (no Thought kind; nearest record kind). Note/Idea/Reference/List/Bookmark map directly; Todo/Reminder → `Note` + action (locked).
- `NodeState` default for **new** captures is `Inbox`; **migrated** items become `Active` (or `Archived` if they had `ArchivedAt`).
- Promoting a node out of `Unclassified` while in `Inbox` moves it to `Active` (a light "I've organised this" signal).
- Deleting a node cascades its tags/collection-profile/event-profile and cleans up Restrict-guarded references (relations, memberships, action targets/contexts, event participants/subject/object/place, user self-node), then logs `Deleted`.
- Reminder/Due dates in the UI are captured at **date** granularity (MudDatePicker); stored UTC.
- The admin migration page lives at `/admin/graph-migration` and is reachable by any authenticated user (single-user personal app); it is safe/idempotent and never destructive.
- A default `Target` ActionContext is created automatically when an action has a primary `TargetNodeId`, so node/collection/person rollups are uniform.
- Tests use EF InMemory for pure logic and real LocalDB for migration/backfill; the `[SqlServerFact]` attribute skips cleanly (with reason) when LocalDB is unavailable.

---

## 11. UX Refinement — Inline Collection Assignment & Node Detail Layout (follow-up)

A focused refinement to the Node create/edit/detail experience. **No architecture change** — it reuses the existing Node-backed Collection / CollectionMembership / ActionItem / Relation / Event services and patterns.

### Goal
Organise a Node into Collections **without leaving the Node screen**, and make it work well for a user with **zero Collections** (create the first collection inline and be added to it in one step).

### Inline collection creation & assignment workflow
- New reusable component **`Components/Shared/CollectionAssignmentPanel.razor`** — always-visible "Collections" area that works in two modes:
  - **Persisted mode** (existing node): shows current memberships as removable chips (with collection type), a **searchable autocomplete** to add to an existing active collection, and a **Create collection** button. Duplicate memberships are prevented; another user's collections never appear; archived collections are excluded from the picker.
  - **Zero-collection empty state**: instead of a dead/disabled picker it shows *"This item is not in any collections yet."* + *"Create your first collection to organize related notes, people, resources, or actions."* and a prominent **Create collection** button.
- New dialog **`Components/Shared/CollectionCreateDialog.razor`** (MudDialog) — required name, optional description, and type (Folder/List/Queue/Plain; Queue/List default to ordered). Client-side duplicate-name check plus authoritative service check.
- On create from a saved node: **`CollectionService.CreateAndAddMemberAsync`** creates the Node-backed collection **and** the membership in a **single `SaveChanges` (one transaction)**, then the panel refreshes and the user stays on the Node with a success snackbar.

### New/unsaved Node behaviour (decision)
The quick-capture screen (`/capture`) hosts the panel in **draft mode**. Pending selections (existing collections and brand-new collection specs) are held **in component state** and are **not persisted** until the Node is saved — so abandoning a draft never creates an orphan collection or a dangling membership. On save the page runs one controlled workflow: create the Node → `ApplyPendingAsync(nodeId)` (adds existing memberships via `AddMemberAsync`, creates new ones via the transactional `CreateAndAddMemberAsync`). **If the save fails, the Node draft (title/body) and all pending collection choices are preserved** and an error is shown; nothing is lost. Rationale: deferring persistence of new collections until node-save is the least-complex reliable option that avoids orphans while keeping form state intact.

### Model / service changes
- `ICollectionService` / `CollectionService`: added `CreateAndAddMemberAsync(title, kind, body, memberNodeId)` (transactional, ownership-checked, duplicate-name-guarded), `NameExistsAsync(name)`, and `GetCollectionSummariesForNodeAsync(memberNodeId)` (memberships with type/order for display). `CreateAsync` now rejects a duplicate **active** collection name (case-insensitive) for consistency with Tag rules; `Collections.razor` handles the error. New UI DTO `Services/CollectionDraft.cs`.
- No schema/migration change: duplicate collection-**name** is a service-level rule; duplicate **membership** remains prevented by the existing composite PK `(CollectionNodeId, MemberNodeId)` **and** the service `exists` check. Queue ordering (`SortOrder`, normalised on move) is unchanged and verified after add/remove.
- `ActionsPanel.OpenCreate()` and `ConnectionsPanel.OpenAdd()` added so the detail page's "Add new" column can trigger their inline add forms (single source of the add form — no duplication).

### Node detail layout refinement
`Components/Pages/NodeDetail.razor` non-edit view reorganised into **three responsive rows, each two columns** (MudGrid `xs=12 md=6`; stacks to one column on small screens; no custom JS, no drag-and-drop):
- **Row 1 — Details | Information:** content/URL/tags + kind & inline promote + primary actions (Edit/Pin/Favorite/Archive/Delete) | created/updated, pin/favorite/archive state, kind·state, at-a-glance **counts** (Collections, Connections, Open actions, Events), and structured details for Collection/Event nodes.
- **Row 2 — Add new | Current:** the **CollectionAssignmentPanel is prioritised** here, plus compact "Create action / Add relationship / Log event / Edit tags" buttons | the Connections+Backlinks panel, Actions panel, and Related-by-tags.
- **Row 3 — History | Context details:** activity log with **Load more** (no silent truncation), completed actions, and events referencing the node | Event details (for Event nodes) and relationship notes/metadata.
Information isn't duplicated across sections (e.g. connection lists live once in Row 2; only their *notes* are surfaced as context in Row 3). The Node route (`/nodes/{id}`) is unchanged.

### Tests — commands & results
```bash
dotnet build Nook.sln            # 0 warnings / 0 errors
dotnet test Nook.Tests/Nook.Tests.csproj
```
**Result: 54 passed, 0 failed, 0 skipped.** Six new `CollectionServiceTests` cover: create-and-add in one step; the draft flow applying existing + new collections after node save; duplicate collection-name rejection (incl. `NameExistsAsync`, case-insensitive); cross-user node rejection in `CreateAndAddMemberAsync`; removing membership keeping both node and collection; and queue ordering staying valid after add/remove. Existing collection/assignment tests continue to pass (multiple collections, duplicate-membership prevention, cross-user add block, scoping, reorder).

**Runtime smoke (fresh LocalDB, demo user has zero collections):** `/nodes/1` renders the three rows (Details/Information/Add new/Current/History/Context details) and the zero-collection empty state ("This item is not in any collections yet" + "Create your first collection"); `/capture` renders the always-visible Collections area with the same empty state. Both return HTTP 200 with **zero server-side exceptions**.

### Safeguards verified
User ownership is enforced for collection creation, assignment, removal and lookup; a user cannot assign a Node to another user's collection or add another user's Node to their collection; duplicate memberships are blocked at both the service and DB (composite PK) levels; create-plus-membership is a single transaction; queue ordering remains correct after add/remove.
