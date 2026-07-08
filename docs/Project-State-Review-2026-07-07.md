# Nook — Project State Review

**Date:** 2026-07-07 · **Branch:** `main` @ `a48f9b9`
**Stack:** .NET 10 Blazor Interactive Server · MudBlazor v9 · EF Core · SQL Server
**Method:** eight parallel deep-readers surveyed one subsystem each (data model, graph services, insights, Nookryptex, UI, auth/infra, tests, deploy/docs); findings were synthesized and the critical claims + build result verified directly against source.

---

## Executive summary

Nook is a single-user **personal knowledge-graph** productivity app with a **capture → classify → connect** workflow. Everything addressable is one `Node` with a display-only `Kind` and a lifecycle `State`; all behaviour lives in related tables — `NodeRelation`/`RelationType` (typed & symmetric edges with inverse-name backlinks), `NodeTag`, `Collection`/`CollectionMembership`, `ActionItem`/`ActionContext`, and `EventDetails`/`Verb` — never in the Kind. It was rebuilt from an overloaded single-`Item` model on 2026-07-02; the legacy tables are retained as an immutable rollback snapshot.

**Current state:** architecturally solid and broadly functional, but **pre-production**. Most subsystems rate *solid* or *functional*; a small, concentrated set of security must-fixes stands between today and a safe public/multi-user launch.

**Verified ground truth:** `dotnet test Nook.sln` → **66 passed, 0 failed, 0 skipped** (LocalDB reachable, so the 4 real-SQL `[SqlServerFact]` migration integration tests ran too — up from 48 tests on 2026-07-02). One non-blocking analyzer warning: `MUD0002` for a `Dense` attribute on `MudTextField` in `ConnectionsPanel.razor`.

| Metric | Value |
|---|---|
| Overall | Functional · Pre-production |
| Build & tests | 66 / 66 (0 failed, 0 skipped) |
| Codebase | ~13k LOC source · +1.5k in tests |
| Surface | 27 pages · ~15 services |
| Must-fix risks | 3 critical, before public launch |

---

## Architecture

.NET 10 Blazor Interactive Server + MudBlazor v9 front-end over an EF Core / SQL Server domain. The core is a Node-centered knowledge graph: one `Nodes` table holds every entity (`Kind` + `State` persisted as readable strings), and semantics come only from related tables — never from `Kind`.

A cohesive service layer (`NodeService`, `RelationService`, `CollectionService`, `ActionService`, `EventService`, plus `AnalyticsService`/`TimelineService`/`ActivityService`/`TagService`/`CryptexService` and the one-shot `GraphMigrationService`) sits between UI and EF Core. Every call resolves the signed-in user via `ICurrentUser` and opens a short-lived `IDbContextFactory` context, so every query is user-scoped and safe for Blazor Server concurrency; ownership is re-validated server-side on every cross-node write.

The UI centres on a rich `NodeDetail` hub composing relation/action/event/collection panels, alongside Capture, Today, Collections, Actions, Events, Analytics, Timeline, Search, and the distinctive **Nookryptex** five-wheel "cryptex" faceted graph browser. ASP.NET Core Identity provides cookie auth (global `[Authorize]`, static-SSR account pages, 30-min security-stamp revalidation). It ships as a multi-stage Docker image via GHCR CI to a docker-compose + nginx/certbot droplet ("alibalib"), sharing a SQL Server container with a separate `Nook` database.

---

## Functionality by area

| Area | Maturity | Summary |
|---|---|---|
| Data & domain model | **Solid** | One Node table; behaviour in related tables. Deliberate SQL-Server-safe cascade-vs-Restrict rules, filtered unique indexes, migration-backed. |
| Core graph services | **Functional** | Uniformly user-scoped CRUD, symmetric-relation canonicalisation, cascade-safe delete. A few latent bugs on edge paths. |
| Item → Node migration | **Functional** | Idempotent, identity-preserving backfill (`IDENTITY_INSERT` + reseed, `MigrationAudit`, parity report) run explicitly from `/admin/graph-migration`. |
| Insights & analytics | **Functional** | Metrics, 8-week trends, day/week timeline, audit log, per-user tags. Correct, but aggregates in memory rather than in SQL. |
| Nookryptex browser | **Solid** | Complete five-wheel faceted cross-filter over a DB-free engine; new nodes inherit the dialed-in "code". Well tested at engine + service level. |
| Blazor UI | **Solid** | Broad, polished capture→classify→connect surface with thoughtful loading/empty/error states. Gaps are polish-level. |
| Auth / Identity | **Functional** | Fully wired & idiomatic for .NET 10 Blazor Server — but insecure *as configured* (see risks). |
| Test suite | **Functional** | 66 behaviour-named tests (62 InMemory + 4 real-LocalDB) cover the whole service layer. Zero UI/auth-flow coverage. |
| Deploy · CI · docs | **Partial** | Solid Docker/compose/nginx path, but a stale README, no CI test gate, and unpersisted DataProtection keys. |

---

## Current state

### Strengths

- Clean Node-centered model: behaviour lives in related tables, SQL-safe cascade rules, filtered unique indexes, fully migration-backed.
- Cohesive, uniformly user-scoped service layer (`ICurrentUser` + short-lived `IDbContextFactory`) that re-validates ownership on every cross-node write.
- Careful, idempotent, identity-preserving Item→Node migration with audit logging and parity validation — deliberately run manually from `/admin/graph-migration`, not auto-run.
- Broad, polished MudBlazor v9 UI (NodeDetail hub, Capture, Today, Collections, Actions, Events, Analytics, Timeline, Search) with consistent loading / empty / error states and correct v9 generic APIs.
- Nookryptex is a complete, distinctive, test-backed feature (pure engine + DB service + scoped UI).
- Strong 66-test suite with a deliberate InMemory / real-LocalDB split so relational-only guarantees are never falsely asserted on InMemory.
- Coherent, Blazor-aware deployment path: SignalR WebSocket proxying, TLS via certbot, immutable short-sha image tags for rollback.

### Weaknesses

- **Password reset is a pre-auth account takeover** — the reset link is rendered on-screen to anonymous requesters.
- DataProtection keys aren't persisted, so every redeploy logs everyone out and breaks antiforgery tokens.
- Seeded demo backdoor + no login lockout + account-enumeration on forgot/register.
- Front-door README is entirely stale — still describes the retired single-Item, no-auth model.
- Latent correctness bugs: an `ActionService` duplicate-key crash, and EF `Include`-dropped-by-projection returning under-populated nodes.
- Dual-model debt not retired (legacy tables, still-registered `ItemService`); no CI test gate; no UI/auth coverage.
- Prod-readiness gaps: no health checks, console-only logging, backups assumed from the shared stack, app connects to SQL as `sa`.

---

## Top risks — must-fix before any real deployment

All three criticals were verified directly against the source.

### 🔴 CRITICAL — Pre-auth account takeover via password reset *(verified)*
`Components/Account/Pages/ForgotPassword.razor` is `[AllowAnonymous]`, generates a real `GeneratePasswordResetTokenAsync` token, and renders the working `/reset-password` link **on the page** (`_resetUrl` → `<MudLink>`) to whoever typed the email — so anyone can reset any known/guessed account. Compounded by user enumeration ("No account found with that email."). **Fix:** wire a real `IEmailSender` (or gate the link) and return a generic "if an account exists…" response.

### 🔴 CRITICAL — DataProtection keys not persisted to the volume *(verified)*
`Program.cs` has no `AddDataProtection().PersistKeysToFileSystem(...)`, so keys default outside the mounted `nookdata` volume. Every image redeploy rotates them, invalidating all auth cookies and antiforgery tokens — mass logout + broken in-flight forms. The Dockerfile comment claims `/app/App_Data` holds them; it does not.

### 🔴 CRITICAL — Seeded demo backdoor + weak auth posture *(verified)*
`Data/DbSeeder.cs` creates `demo@nook.local` / `Demo123!` (`EmailConfirmed=true`) on any empty DB in **every** environment (`Program.cs` calls `DbSeeder.InitializeAsync` unconditionally). Login uses `lockoutOnFailure:false` (no lockout). `/admin/graph-migration` sits in the primary nav for any signed-in user. **Fix:** gate the seed behind `IsDevelopment`, enable Identity lockout, put the admin page behind a policy.

### 🟠 HIGH — Stale README & no CI test gate
The README still says `ItemType` and "v1 is single-user with no authentication," naming deleted pages/components — actively misleading every new contributor. Separately, `.github/workflows/build-and-push-image.yml` only builds & pushes the image, so the 66-test suite never runs on a release.

### 🟠 HIGH — Latent service-layer correctness bugs
- `ActionService.CreateAsync` throws a duplicate composite-key at `SaveChanges` when an explicit Target-role `ActionContext` equals `TargetNodeId` (it checks `db.ActionContexts` via a round-trip that cannot see the just-added, unsaved rows — should check `.Local`).
- `EventService.GetTimeline`/`GetEventsForNode` and `CollectionService.GetMembersAsync` end `.Include(...).Select(entity)`; once a `Select` projects to an entity, the `Include`s are ignored, so returned nodes silently lack tags / verbs / event details.

---

## Recently completed

- **Knowledge-graph rebuild** (commit `45c2676`, 2026-07-02) — Node model, full service layer, EF migration `20260702232156_AddKnowledgeGraph`, and graph UI replacing the single-Item model.
- **Nookryptex** cryptex-style faceted graph browser at `/nookryptex`, merged as PR #1 (commit `9111bf1`) with pure-engine and service tests.
- Removal of legacy Item pages that had resurfaced and caused ambiguous routes (commit `f62dd8c`).
- On-screen (no-email) **password-reset flow** (commit `f1a2711`), plus a show-password toggle on registration.
- **Multi-user ASP.NET Identity auth** together with Timeline, Analytics, and Activity-Log features (commit `57bb11a`).
- **"alibalib" deployment** landed whole (commit `1bcb3d5`): Dockerfile, docker-compose, GHCR CI workflow, and the `deployment/` runbook (nginx, certbot, `.env` template).
- Adoption of `BlazorDisableThrowNavigationException=true` with `IdentityRedirectManager` rewritten to redirect without throwing `NavigationException`.

---

## Work in progress / in-flight

- **The Item→Node cutover itself.** Legacy `Item`/`ItemTag`/`ItemLink` tables and DbSets are retained as an immutable rollback snapshot; the backfill is run manually from `/admin/graph-migration` pending validation — the drop-legacy migration is deliberately not yet written.
- **Legacy `ItemService` teardown.** `ItemService`/`IItemService`/`ItemFilter` are still DI-registered and fully tested but disconnected from the UI (pages removed in `f62dd8c`) — a teardown that has started but not finished.
- **User-defined custom relation types and verbs.** `RelationType.UserId` and `Verb.UserId` columns exist but are documented as "reserved for future custom types" and are unused.
- **Migration-leftover naming cleanup.** `AnalyticsModel.TotalItems/OpenItems/CompletedItems`, `TagSummary.ItemCount`, and `ActivityLog.ItemTitle` still carry old "Item" vocabulary, and `ActivityService` still exposes parallel legacy `LogAsync(ItemId)` alongside `LogNodeAsync(NodeId)`.

---

## Improvement roadmap

### Now — before launch
- **Close the password-reset takeover** — real email sender or gated link; generic response to kill enumeration.
- **Persist DataProtection keys** — `AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo("/app/App_Data")).SetApplicationName("Nook")`.
- **Harden the auth surface** — remove the demo backdoor in prod, enable Identity lockout, gate `/admin/graph-migration` behind an admin policy.
- **Rewrite the stale README** to match the Node graph + multi-user reality — highest-value, lowest-effort fix.
- **Fix the two service-layer bugs** — dedupe the auto-Target context against `.Local`; project to DTOs (or drop the `Select`) so eager-loaded `Include`s populate.

### Next
- **CI build + test gate** — run the 66-test suite before the image is built/pushed on main.
- **Health checks** — `AddHealthChecks().AddDbContextCheck<NookContext>()` at `/health` + a compose healthcheck.
- **Fix `nvarchar(max)` UserId columns** — migrate `CollectionMembership.UserId` and `EventParticipant.UserId` to indexable `nvarchar(450)` with covering indexes (matching `ActionContext`).
- **UI polish bugs** — `SearchPage` re-search guard (`_query is null` blocks repeat searches), persist dark mode, dead `FocusOnNavigate` selector (`h1` that no page renders), unstyled `NotFound`.
- **Retire the legacy Item model** — drop tables + `ItemService` once `ValidateAsync` passes in production.

### Later
- **Push analytics into SQL** — server-side `GROUP BY`; add an `ActivityLogId` tiebreaker so Timeline ordering / the `Take(500)` cutoff is deterministic.
- **bUnit + auth-flow tests** — cover the ~40 Razor components and every auth flow (the "builds clean, fails at runtime" surface), plus the real `HttpContext`-backed `CurrentUser`.
- **Unify the verb concept** — one model for verbs (`ActionVerb` enum vs the `Verb` table); enforce the Kind↔profile invariant at the DB level.
- **Production hardening** — least-privilege SQL login (stop connecting as `sa`), OTel instrumentation (the `OTEL_*` env is wired but unused), and an owned Nook-specific backup/restore drill.

---

## Appendix — per-subsystem notes

### Data & domain model — *Solid*
Coherent, migration-backed schema. Cascade/delete is deliberate and SQL-Server-safe: 1:1 profiles (`Collection`, `EventDetails`) and true child tables cascade from their owner, while every reference FK is `Restrict` to avoid multiple-cascade-path errors. **Nits:** cross-cutting invariants (Kind↔profile, self/cross-user relations) are app-only, not DB-enforced; `CollectionMembership.UserId` / `EventParticipant.UserId` are `nvarchar(max)` (unindexable); `Node.UpdatedAt` isn't bumped when relations/tags/memberships/actions change, so "recently touched" misses graph activity; the verb concept is modeled twice.

### Core graph services — *Functional*
Consistent user-scoping, ownership re-validation on every cross-node write, symmetric canonicalisation, cascade-safe delete. **Watch:** the `ActionService.CreateAsync` duplicate-key crash; `Include`-dropped-by-projection in `EventService`/`CollectionService`; N+1 ownership checks (`OwnsNodeAsync` per element); `AddRelationAsync` check-then-insert isn't atomic (relies on the unique index; the resulting `DbUpdateException` is unhandled → 500).

### Insights & analytics — *Functional*
`AnalyticsService`, `TimelineService`, `ActivityService`, `TagService` all function and correctly avoid the EF projected-ordering gotcha. **Watch:** Analytics materializes every node and action in memory (belongs in SQL); Timeline ordering lacks a tiebreaker (nondeterministic cutoff); a node's history may miss pre-migration `ActivityLog` rows (written with `ItemId`, `NodeId` null); `GetOrCreateAsync` has a TOCTOU race on the tag unique index.

### Nookryptex — *Solid*
Complete `/nookryptex` five-wheel (Kind/Tag/Collection/State/People) cryptex: a pure DB-free `CryptexEngine` cross-filters in memory with own-ring-ignoring counts; `CryptexService` one-shot loads the dataset and stamps new nodes with the dialed-in code. Well tested. **Rough edges:** case-sensitive filtering vs case-insensitive wheel dedup; no match ordering; dataset is loaded once (stale until reload); the selected Person's own node is excluded from People-wheel results.

### Blazor UI — *Solid*
Rich `NodeDetail` hub; Capture with draft-mode collection assignment; Today dashboard; Collections with ordered membership; Actions with checklists/filters; Events; Analytics charts; Timeline; Log; global Search. Correct MudBlazor v9 generic APIs throughout. **Polish gaps:** dark mode not persisted; `FocusOnNavigate Selector="h1"` never matches (pages render `Typo.h4`); `SearchPage` re-search guard bug; unstyled `NotFound` stub; `NavMenu.OnLocationChanged` re-fetches tag counts on every navigation.

### Auth / Identity — *Functional (security-critical)*
Cookie-based Identity is fully wired and idiomatic: global `[Authorize]`, custom `/login`, static-SSR account pages, login/register/logout, 30-min security-stamp revalidation, all services DI-registered. **But:** the on-screen password reset is a pre-auth account takeover (verified); login lockout disabled; demo credentials seeded in every environment; user enumeration on forgot/register; relaxed password policy (length 6, no complexity); `IdentityUserAccessor` registered but unused (redirects to a non-existent `Account/InvalidUser`).

### Test suite — *Functional*
66 behaviour-named xUnit tests (62 EF InMemory + 4 real-LocalDB `[SqlServerFact]` migration tests). Thoughtful InMemory/LocalDB split via the custom `SqlServerFact` attribute that skips-with-reason when `(localdb)\MSSQLLocalDB` is unreachable. Strong, multi-user-scoped coverage of the whole service layer + the migration's identity/idempotency guarantees. **Gaps:** zero coverage of the ~40 Razor components/pages, the real `HttpContext` `CurrentUser`, search, the Nookryptex UI, and — most notably — every auth flow.

### Deployment · CI · docs — *Partial*
Solid machinery: multi-stage Docker image, GHCR CI on push to main, docker-compose + nginx/certbot correctly handling Blazor SignalR WebSockets, short-sha rollback. The three knowledge-graph docs are exemplary and current. **But:** the README is comprehensively stale; DataProtection keys aren't persisted to the mounted volume; no health checks; CI never runs the test suite; console-only logging (OTel env wired but app not instrumented); backups merely assumed from the shared `awblazor` stack; app connects as `sa`; `.github/copilot-instructions.md` is irrelevant Azure boilerplate.

---

*Generated 2026-07-07. Point-in-time — re-verify against current code before acting on specific file/line claims.*
