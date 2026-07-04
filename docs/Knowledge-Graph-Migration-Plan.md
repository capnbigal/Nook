# Nook Knowledge-Graph Migration Plan

> Companion to `docs/Knowledge-Graph-Redesign.md` (revised). This plan is **additive, reversible, and single-authoritative**: new tables are created beside the existing ones, data is backfilled (IDs preserved), validated, cut over to **one** write model, and legacy tables are dropped only in a separate later migration after sign-off. **No uncontrolled dual writes.**
>
> **Implemented 2026-07-02** — see `docs/Knowledge-Graph-Implementation-Report.md`. The migration `20260702232156_AddKnowledgeGraph` and an idempotent, explicitly-run `GraphMigrationService` (admin page `/admin/graph-migration`) deliver this plan. **One material change from §4:** per the locked implementation decision there is **no one-way new→legacy projection during soak** — legacy `Item*` tables are an immutable snapshot and post-cutover rollback is a verified database restore (or deliberate export/import), not a flag flip.

## Executive Summary

The migration is ordered for safety: **back up → add new tables → seed → backfill (preserve IDs) → validate → read-only validation period → cut over to one authoritative model → keep legacy until sign-off → retire legacy later.** Existing `Item` rows become `Node` rows with the **same primary-key values**, so every `ItemLink`, `ItemTag`, `ParentItemId`, and `ActivityLog.ItemId` maps by identity — no crosswalk table. Tags, auth, and per-user scoping carry over unchanged.

The only shape-changing transform is the **approved Todo/Reminder split**: each legacy Todo/Reminder becomes a **knowledge Node plus one or more attached Actions**, so `Status/Priority/DueDate/ReminderDate/CompletedDate` never sit in an ambiguous transitional state — Phase 1 ships a minimal `Action` table specifically to receive them. Backfill is **mechanical only**: it invents no relationships or classifications beyond what legacy data determines, and uses `Unclassified` where a legacy type was merely organizational and cannot be safely mapped. Nothing is deleted until parity and journey tests pass.

---

## 1. Existing-to-New Mapping

**Disposition key:** **Preserve** · **Migrate** (1:1 copy) · **Transform** (reshape) · **Deprecate** (stop using; keep temporarily) · **Retire-after-verify** (drop only post-validation).

| Current structure | Target | Disposition | Notes |
|---|---|---|---|
| `Item` (all rows) | `Node` (`NodeId = ItemId`) | **Migrate + Transform** | Common fields copy directly (Title, Body, Url, IsPinned, IsFavorite, ArchivedAt, CreatedAt, UpdatedAt). `ItemType`→`Kind`. `State = Archived` if `ArchivedAt` set, else **`Active`** (migrated rows were in use, not Inbox). Action fields split out (below). |
| `ItemType = Note / Thought` | `Node.Kind = Note` | **Transform** | Thought→Note (or keep a `Thought` kind — default Note). |
| `ItemType = Idea / Observation / Journal / Reference` | `Node.Kind =` same | **Transform** | Straight mapping. |
| `ItemType = Bookmark` | `Node.Kind = Bookmark` (`Url` on Node) | **Migrate** | `ResourceProfile` only if/when needed (P2). |
| `ItemType = List` | `Node.Kind = List` (record); children via `contains` | **Transform** | **Decision (redesign Open #2):** keep as a record List; **do not** invent a Collection or membership. Promotable later. |
| `ItemType = Todo` | `Node.Kind = Note` **+ `Action` (Kind=Task)** | **Transform** | See §2.3 backfill rules. Node keeps content/tags/links; Action carries Status/Priority/DueDate/CompletedDate. |
| `ItemType = Reminder` **or** any row with `ReminderDate` | `Node` **+ `Action` (Kind=Reminder, `RemindAt=ReminderDate`)** | **Transform** | See §2.3. A non-Reminder row that merely has `ReminderDate` also gains a Reminder Action. |
| `Item.Status / Priority / DueDate / CompletedDate` | `Action` fields | **Transform** | Move to the Task Action; not stored on Node. |
| `Item.ReminderDate` | `Action.RemindAt` | **Transform** | On the Reminder Action. |
| `Item.IsPinned / IsFavorite / ArchivedAt` | `Node.IsPinned / IsFavorite / ArchivedAt` (+ `State`) | **Preserve** | Node-level UI/lifecycle state stays on Node. |
| `Item.ParentItemId` | `Relation` (parent `contains` child) | **Transform** | One `contains` relation per non-null parent; single-parent tree fully reproducible. |
| `ItemLink` | `Relation` | **Migrate** | Same endpoint IDs; `LinkType` free text → `RelationType` (map known words else default **related to**; keep original string in `Relation.Note`); preserve `CreatedAt`. |
| `Tag` | `Tag` | **Preserve** | Unchanged. |
| `ItemTag` | `NodeTag` | **Migrate** | `(ItemId,TagId)`→`(NodeId,TagId)`; identical values. |
| `ActivityLog` | `ActivityLog` (retargeted) | **Migrate** | `ItemId`→`NodeId` (nullable; same values); denormalized `ItemTitle` preserved; behavior unchanged. |
| Timeline / Analytics data | recomputed from `Node`/`Action`/`ActivityLog` | **Preserve (derived)** | Nothing stored to migrate. |
| Auth / `ApplicationUser` / `UserId` ownership | unchanged; new tables add `UserId` (Restrict) | **Preserve** | Optional lazy `SelfNodeId` (nullable) added P2. |
| Identity tables (AspNet*) | unchanged | **Preserve** | Out of scope. |

**New tables with no legacy source:** `RelationType` (seeded), `Collection`+`CollectionMembership`, minimal `Action` (P1); `ActionContext`, `EventDetails`/`EventParticipant`/`Verb`, profiles (P2+).

**Content-retention guarantee:** existing Notes, Thoughts, Ideas, Lists, References, and Bookmarks retain their content even if never connected to an Entity, Collection, Event, or Action. **No invention:** backfill derives only what legacy rows determine; `Unclassified` is used only where a legacy type was purely organizational and cannot be safely mapped.

---

## 2. Migration Strategy

### 2.1 Ordered, additive, reversible
1. **Back up first** and **restore-test** to a scratch DB before anything runs. On the shared deployment, confirm the instance-wide backup captured `Nook`.
2. **Add new tables beside legacy** in one additive EF migration (`Node`, `RelationType`, `Relation`, `NodeTag`, `Collection`, `CollectionMembership`, minimal `Action`). **No** `Item*` column is dropped here.
3. **Seed reference data idempotently** — system `RelationType`s and `Verb`s (`WHERE NOT EXISTS`).
4. **Backfill Nodes preserving IDs** (§2.2).
5. **Backfill** tags, links, parent-child (`contains`), and **minimal Actions** (Todo/Reminder split, §2.3).
6. **Validate** counts, ownership, references, behavior (§3).
7. **Read-only validation period** (§4): new graph views render read-only; legacy remains authoritative.
8. **Cut over to one authoritative write model** (§4): new Node/Relation/Action tables become the source of truth; legacy pages redirect / read-only.
9. **Keep legacy data** until verification and sign-off.
10. **Retire legacy** (`Item`, `ItemLink`, `ItemTag`) only in a **separate later migration**.

### 2.2 ID preservation
- `INSERT INTO Node (NodeId, …) SELECT ItemId, … FROM Item` under `SET IDENTITY_INSERT Node ON`.
- **Reseed** the Node identity to `MAX(NodeId)` via `DBCC CHECKIDENT` afterward so new nodes don't collide.
- All references that used `ItemId` are valid against `NodeId` unchanged. (Fallback if identity-preserve is ever unsafe: an `ItemId→NodeId` map table + translated references — not the default.)

### 2.3 Exact Todo / Reminder / Action backfill rules
Let `S(x)` map `ItemStatus`→`Action.Status` (Open→Open, InProgress→InProgress, Done→Done, Cancelled→Cancelled). For every legacy `Item` (already copied to a Node with the same ID):

1. **`ItemType = Todo`** → create **Action(Kind=Task)**: `Title=Item.Title`, `Status=S(Item.Status)`, `Priority=Item.Priority`, `DueDate=Item.DueDate`, `CompletedAt=Item.CompletedDate`, `Verb=null`, `TargetNodeId=NodeId`, `UserId=Item.UserId`. Node `Kind=Note`.
2. **`ItemType = Reminder`** → create **Action(Kind=Reminder)**: `Title=Item.Title`, `RemindAt=Item.ReminderDate`, `Status=S(Item.Status)`, `CompletedAt=Item.CompletedDate`, `TargetNodeId=NodeId`, `UserId=Item.UserId`.
3. **`ItemType = Todo` AND `ReminderDate` not null** → create **both** a Task Action (rule 1) **and** a Reminder Action (rule 2). Two Actions, one Node.
4. **Any other `ItemType` with `ReminderDate` not null** → keep the mapped record `Kind` and add a **Reminder Action** (rule 2).
5. **Any other `ItemType` with `DueDate` not null but no todo/reminder semantics** → **do not** invent a Task (no invention rule). `DueDate` on a non-actionable legacy row is dropped with a logged note in the validation report; if a user relied on it, they re-add an Action post-migration. *(If you prefer to preserve these, the alternative is a Task Action for any row with a `DueDate`; flagged as an Open Decision, default = do not invent.)*
6. **Completed state:** preserved on the **Action** (`Status=Done`, `CompletedAt=CompletedDate`). The **Node is never marked done** (Nodes have no done state). `Node.State = Archived` iff `Item.ArchivedAt` set, else `Active`.

Every rule is deterministic and idempotent (guarded by `WHERE NOT EXISTS` on a natural key such as `(UserId, TargetNodeId, Kind, Title)` during backfill).

### 2.4 Authoritative model per stage (no dual writes)

| Stage | Authoritative writer | New tables | Legacy pages |
|---|---|---|---|
| A. Additive + backfill | **Legacy `Item`** | populated, **not written by UI** | normal |
| B. Read-only validation | **Legacy `Item`** | **read-only** new views for comparison | normal |
| C. Cutover | **New Node/Relation/Action** | **authoritative (all writes)** | **redirect / read-only**; a one-way **new→legacy projection** runs only during the soak window for rollback safety |
| D. Post-sign-off | **New** | authoritative | retired in a separate migration |

The Stage-C **new→legacy projection** is a single controlled direction (new is truth; legacy is a shadow copy for rollback) — it is **not** two independent writers. After sign-off the projection stops and legacy is retired.

### 2.5 Idempotency & feature flag
Every backfill step is re-runnable (`NOT EXISTS`/marker guarded). New UI lives behind `KnowledgeGraphUi`; Stages B/C are flag-controlled. Because Step 2 is additive, pre-cutover rollback = down-migration (drop new tables); the untouched `Item*` tables still run the app.

---

## 3. Testing & Validation Plan

### 3.1 SQL Server integration tests (not just InMemory)
Migration correctness **must** be tested against **real SQL Server** (LocalDB/container), because InMemory does not model `IDENTITY_INSERT`, identity reseeding, filtered/unique indexes, FK `Restrict` behavior, or NULL-in-unique-index semantics. Cover:
- Additive migration applies; schema + seed `RelationType`s/`Verb`s present.
- `IDENTITY_INSERT` backfill + `DBCC CHECKIDENT` reseed; new inserts don't collide with preserved IDs.
- **Filtered unique indexes on `RelationType`** enforce one system type per name (`UserId IS NULL`) and one per-user type per name (`UserId IS NOT NULL`).
- FK `Restrict` blocks user/node deletes that would orphan; two-FK-to-Node tables (Relation, CollectionMembership) don't create multiple cascade paths.
- Unique constraints: `(UserId,Source,Target,RelationTypeId)`, `NodeTag (NodeId,TagId)`, `CollectionMembership (CollectionNodeId,MemberNodeId)`.
- Backfill **idempotency** (run twice → identical state, no duplicates).

### 3.2 Unit tests (xUnit; InMemory acceptable for pure logic)
- **Relations:** create/duplicate prevention, self-link rejection, symmetric canonicalization, inverse-label rendering, backlink query.
- **Actions:** attach to any Node kind; complete → `Status=Done`+`CompletedAt` without mutating the target Node; "open actions for node X"; a Node spawning **multiple** dated Actions over time; Reminder `RemindAt`.
- **Reusable-intent workflow:** reusable Node → create dated Action → complete it → Node unchanged → create a second Action later (no reuse/reopen of the completed one).
- **Collections:** node-backed 1:1; membership uniqueness; one Node in many collections; ordering.
- **Events (P2):** subject/object/place/participant wiring; "introduced by" optional Relation; free-text-only event valid.

### 3.3 Cross-user isolation (critical)
Two seeded users; user B cannot read/modify user A's **Nodes, Relations, NodeTags, Collections, CollectionMemberships, Actions, ActionContexts (P2), or Events (P2)** — parameterized over every new service. Service-level `UserId` guards enforced even where a composite DB constraint can't.

### 3.4 Migration count/parity & ID-mapping
- `count(Node) == count(Item)`; `Node.NodeId == Item.ItemId` for every row.
- `count(NodeTag) == count(ItemTag)`; per-node tag sets match.
- `count(Relation from ItemLink) == count(ItemLink)`; direction + `CreatedAt` preserved.
- `count(contains Relations) == count(Item WHERE ParentItemId IS NOT NULL)`.
- `count(Task Actions) == count(Item WHERE ItemType=Todo)`; `count(Reminder Actions) == count(Item WHERE ItemType=Reminder OR ReminderDate IS NOT NULL)`; Todo+ReminderDate rows produce two Actions.
- Completed parity: `count(Action WHERE Status=Done) == count(Item[actionable] WHERE Status=Done)`.
- `count(ActivityLog)` unchanged; every `NodeId` resolves or is null exactly as the old `ItemId`.

### 3.5 User-journey tests (the 7 scenarios)
Automate each redesign §7 scenario end-to-end at the service level, asserting resulting rows, relations, memberships, Actions/Events, and that aggregate views ("everything about a person") return the expected sets. Includes the **reusable Node → multiple dated Actions** journey explicitly.

### 3.6 Validation script (pre/post)
A runnable check printing old-vs-new counts per user and failing on any mismatch; run on a **restored copy** before touching production data, and again after backfill during Stage B.

---

## 4. Cutover, Dual-Run, and Rollback

### 4.1 No uncontrolled dual writes
Old and new pages must **never** write independently to separate models. Exactly one model is authoritative at any time (§2.4). During Stage C the only cross-model write is the **one-way new→legacy projection**, purely to keep a rollback shadow current.

### 4.2 Rollback accounting for post-cutover data
- **Pre-cutover (Stages A/B):** rollback = down-migration (drop new tables); legacy `Item*` unaffected; optionally restore the Step-1 backup.
- **Post-cutover (Stage C, during soak):** new data is written to new tables **and** shadow-projected to legacy. Rollback = disable the flag and switch authority back to legacy, which is current **because of the projection** — so data created after cutover is not lost. (Flipping the flag alone, without the projection, would lose post-cutover data — hence the projection is mandatory during soak.)
- **After sign-off:** the projection stops; this is the declared point of no return; legacy is retired in a separate migration. Any later rollback is a restore-from-backup exercise, not a flag flip.

### 4.3 Cutover checklist
- [ ] Backup taken and restore-tested.
- [ ] Additive migration applied; new tables + seeds present; `Item*` intact.
- [ ] Backfill run; validation script green (counts + parity + ID mapping).
- [ ] SQL Server integration + unit + isolation + journey tests pass.
- [ ] Stage B: new views read-only alongside legacy; parity re-checked on live data.
- [ ] Stage C: switch authority to new model; legacy pages redirect/read-only; **new→legacy projection running**.
- [ ] Soak window clean → sign-off; stop projection.
- [ ] Separate later migration: fully retarget `ActivityLog`, **drop `Item`, `ItemLink`, `ItemTag`**.
- [ ] Rollback rehearsed for both pre- and post-cutover cases.

---

## 5. SQL Server Integrity Requirements (summary)
- **Filtered unique indexes** for `RelationType` and `Verb` where `UserId` is nullable (system vs user).
- **`DeleteBehavior.Restrict`** on all `UserId` FKs and on both Node FKs of `Relation` and `CollectionMembership` (avoids multiple-cascade-path errors — the known Nook trap).
- **Unique constraints:** relations `(UserId,Source,Target,RelationTypeId)`; `NodeTag (NodeId,TagId)`; membership `(CollectionNodeId,MemberNodeId)`; 1:1 profiles/`Collection`/`EventDetails` via unique `NodeId`.
- **Indexes for the hot paths:** outgoing relations `(SourceNodeId)`; backlinks `(TargetNodeId,RelationTypeId)`; action rollups `(UserId,Status,DueDate)` and `(TargetNodeId)`; reminders `(UserId,RemindAt)`; membership `(CollectionNodeId,SortOrder)`; timeline/events `(UserId,OccurredAt)` (P2); dashboard `(UserId,State)` and `(UserId,UpdatedAt)`.
- **Service-level scoping** wherever a composite DB constraint can't span users (e.g. ensuring both relation endpoints share `UserId`).

---

## 6. Migration Task Order (maps to redesign's First-10)
1. Backup + restore-test + validation script + baseline count tests + **SQL Server integration harness** (Phase 0).
2. Confirm the small Todo/Reminder mapping details; lock backfill rules (§2.3).
3. Additive migration: Node/RelationType/Relation/NodeTag/Collection/CollectionMembership/**minimal Action**.
4. Seed RelationTypes/Verbs; backfill Items→Nodes (ID-preserve, reseed).
5. Backfill ItemTag→NodeTag, ItemLink→Relation, ParentItemId→contains, **Todos/Reminders→Node+Action**.
6. NodeService/RelationService/CollectionService/ActionService (scoped; dedup/self-link/cross-user guards; canonicalization).
7. Full validation: parity, ID mapping, isolation, dedup, canonicalization, Todo/Reminder backfill, reusable-Node→dated-Action, idempotency — on real SQL Server.
8. NodeDetail with Connections/Backlinks + Create Action; `/items/{id}` redirects.
9. Node-backed Collections UI + Inbox/All/Unassigned views.
10. Retarget capture/search/tags; Stage B read-only validation → Stage C single authoritative cutover with new→legacy projection; then Phase 2 (ActionContext, checklists, Events) with its own additive migration + backfill.
