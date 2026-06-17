# Nook — Multi-User, Auth, Timeline, Analytics & Activity Log

**Date:** 2026-06-16
**Status:** Approved design, ready for implementation planning
**Builds on:** `2026-06-16-personalhub-design.md` (the original single-user v1)

## Summary

Nook moves from a single-user, no-auth personal app to a **multi-user** app with full
authentication. Every `Item` and `Tag` becomes owned by a user. On top of that foundation
we add four user-facing surfaces — a **Homepage/landing page**, an activity-driven
**Timeline** with auto-generated "shoutout" summary cards, an **Analytics** dashboard, and
an **Activity Log** — plus a **Tags section in the nav menu** that links to tag-filtered
item views.

## Goals

- Real multi-user app with **open registration**; each user has a private space of items and tags.
- Full **ASP.NET Core Identity** (email + password, cookie auth), with custom MudBlazor-styled
  Login and Registration pages instead of the scaffolded Razor pages.
- Per-user data isolation enforced in the service layer with no cross-user leakage.
- An **activity audit trail** recorded on every mutating item operation, powering both the Log and the Timeline.
- New pages: Homepage, Login, Registration, Timeline, Analytics, Log.
- Nav menu lists the current user's tags, each linking to that tag's items.

## Non-Goals

- External/social login (Google, etc.) — email+password only for now.
- Roles / admin console / sharing items between users.
- Email confirmation / password reset flows (can follow later; not required for v1 of multi-user).
- Real-time/live updates of analytics or timeline (page-load computed is fine).

## Architecture

### 1. Authentication (ASP.NET Core Identity)

- Add package `Microsoft.AspNetCore.Identity.EntityFrameworkCore`.
- New entity `ApplicationUser : IdentityUser` in `Models/`.
- `NookContext` changes from `DbContext` to `IdentityDbContext<ApplicationUser>`; its
  `OnModelCreating` must call `base.OnModelCreating(builder)` first, then keep existing config.
- Register Identity in `Program.cs`:
  - `AddIdentityCore<ApplicationUser>` (or `AddIdentity`) with `AddEntityFrameworkStores<NookContext>`
    and `AddSignInManager`.
  - Cookie authentication; `AddAuthentication`/`AddAuthorization`.
  - A `CascadingAuthenticationState` (via `AddCascadingAuthenticationState`) and an
    `AuthenticationStateProvider` suitable for Interactive Server (the
    `IdentityRevalidatingAuthenticationStateProvider` pattern).
  - `app.UseAuthentication()` before `app.UseAuthorization()` in the pipeline.
- Password options kept reasonable defaults; no email confirmation required to sign in.
- Sign-in/sign-out must run on a real HTTP request (not over the SSR/circuit). Use minimal API
  endpoints (e.g. `POST /account/login`, `POST /account/logout`, `POST /account/register`) that the
  MudBlazor forms post to, OR the standard Blazor Identity endpoint pattern. The MudBlazor pages
  collect input and submit a real form post so the auth cookie is written.

### 2. Per-user data ownership

- Add `public string UserId { get; set; }` + `ApplicationUser? User` navigation to **`Item`**.
- Add `public string UserId { get; set; }` + `ApplicationUser? User` navigation to **`Tag`**.
  - Tags are per-user: the same tag name may exist independently for two users. The existing
    unique constraint on tag name (if any) becomes unique **per (UserId, Name)**.
- `ItemTag` and `ItemLink` inherit ownership transitively through their `Item`; no direct `UserId`
  needed on them, but service queries always start from the user's items.
- **`ICurrentUser`** service: resolves the current user's id from the cascaded
  `AuthenticationState` (and `UserManager` where needed). Injected into `ItemService`/`TagService`
  so all reads/writes are scoped. This indirection keeps services unit-testable without a live HTTP context.
- Every query in `ItemService` and `TagService` filters by `UserId == currentUser.Id`. Every create
  stamps `UserId`. Update/delete operations first verify the row belongs to the current user
  (defense-in-depth against id tampering).

### 3. Activity log

- New entity `ActivityLog`:
  - `int Id`
  - `string UserId` (FK → ApplicationUser)
  - `int? ItemId` (nullable so a log row survives item deletion)
  - `string ItemTitle` (denormalized snapshot for display after deletion)
  - `ActivityType Type` — enum: `Created, Updated, Completed, Archived, Unarchived, Tagged, Deleted`
  - `DateTime Timestamp`
  - `string? Detail` (e.g. "added tag 'work'", "status Open → Done")
- New `IActivityService` with `LogAsync(...)` and query methods (by user, filterable by type/date range).
- `ItemService` calls `IActivityService.LogAsync` inside each mutating operation
  (create, update — **including edits**, complete, archive/unarchive, tag changes, delete).
- This single table is the source of truth for both the Log page and the Timeline events.

### 4. Migration & seeding

- One new EF Core migration adding: Identity tables, `UserId` on `Item` and `Tag`,
  the `ActivityLog` table, and the updated per-user tag uniqueness.
- `DbSeeder`:
  - Ensures a **demo user** exists (fixed email/password, e.g. `demo@nook.local`).
  - Assigns all seeded items and tags to the demo user's id.
  - Existing-data note: the dev database can be dropped and reseeded; no production data migration concern.
- Migrations remain auto-applied at startup in Development.

## Pages & Components

### Homepage — `/` (anonymous)
- Public landing page: short pitch of what Nook does, feature highlights, **Login** and **Register** CTAs.
- If the visitor is already authenticated, redirect to `/dashboard`.

### Login — `/login` (anonymous)
- MudBlazor form (email, password, remember-me), validation, error display on bad credentials.
- Submits a real form post to the login endpoint; on success redirects to `/dashboard` (or `returnUrl`).
- Link to `/register`.

### Registration — `/register` (anonymous)
- MudBlazor form (email, password, confirm password), validation.
- Creates the account, signs the user in, redirects to `/dashboard`.
- Link to `/login`.

### Timeline — `/timeline` (authorized)
- `MudTimeline`, newest-first, of the current user's `ActivityLog`, grouped by day.
- **Shoutout cards** interleaved at period boundaries (e.g. between weeks): auto-generated highlight
  callouts summarizing the adjacent stretch of activity. Examples:
  - "12 todos completed this week 🎉"
  - "Most productive day: Tuesday (8 items)"
  - "Reached 50 notes"
  - "5-day capture streak"
- `ITimelineService` builds an ordered list of timeline entries (events + shoutouts) from the activity
  log and item data. Shoutout rules are deterministic and unit-testable (no LLM).

### Analytics — `/analytics` (authorized)
Backed by `IAnalyticsService`; rendered with `MudChart` and MudBlazor stat cards. Four areas:
1. **Counts & breakdowns** — items by type, by status, by priority; open vs completed; tag-usage frequency.
2. **Trends over time** — items created per week/month; completion rate over time; todos closed per week.
3. **Productivity stats** — completion rate %, avg time-to-complete, overdue count, current streak, busiest day/week.
4. **Tag insights** — top tags, items-per-tag leaderboard, untagged item count.
All scoped to the current user.

### Log — `/log` (authorized)
- Dense, filterable chronological feed (table/list) of the current user's `ActivityLog`.
- Filters: activity type, date range. Newest-first, paged.

### Nav menu changes
- New links: **Timeline**, **Analytics**, **Log**.
- **Tags group** (`MudNavGroup`, collapsible) listing the current user's tags with item counts;
  each tag links to the tag-filtered items view at `/items?tag={name}`.
  - Confirm the `Items` page reads a `tag` query parameter and applies it to `ItemFilter`; wire it if absent.
  - A "View all tags" entry links to the existing `/tags` page.
- An account/user section showing the signed-in user with a **Logout** action.
- Nav menu (and the rest of the app shell) only renders for authenticated users; anonymous users see the homepage/login/register shell.

## Services (new)

| Service | Responsibility |
|---|---|
| `ICurrentUser` | Resolve current user id from auth state for the service layer. |
| `IActivityService` | Write activity log rows; query them (by user, type, date). |
| `ITimelineService` | Compose timeline entries (events + generated shoutouts) for a user. |
| `IAnalyticsService` | Compute all analytics aggregations for a user. |

Existing `IItemService` / `ITagService` gain user-scoping and activity logging.

## Data Flow

1. User registers/logs in → auth cookie set → `AuthenticationStateProvider` exposes the principal.
2. `ICurrentUser` reads the id; injected into services.
3. Item mutation → `ItemService` writes the change **and** an `ActivityLog` row (scoped to user).
4. Timeline/Log read `ActivityLog`; Analytics reads `Item`/`Tag`/`ActivityLog` — all filtered by user id.

## Error Handling

- Unauthenticated access to `[Authorize]` pages redirects to `/login?returnUrl=...`.
- Login/registration failures show inline form errors (no stack traces).
- Service methods that receive an id not owned by the current user return not-found/forbidden
  rather than acting on it.
- Keep the existing EF Core gotcha in mind: never `OrderBy` a property of a projected record —
  order on the source before `Select` (relevant for analytics/tag-count projections).

## Testing

- **User scoping:** items/tags/activity queries never return another user's rows (seed two users, assert isolation).
- **Activity logging:** each mutating `ItemService` operation writes exactly one correct `ActivityLog` row.
- **Shoutouts:** `ITimelineService` emits the expected shoutouts for crafted activity datasets (deterministic).
- **Analytics:** `IAnalyticsService` aggregations (counts, trends, completion rate, streak, tag insights)
  return correct numbers for known fixtures.
- Service tests run against EF Core InMemory or SQLite; verify pages actually load (prerender) to catch
  untranslatable LINQ, per the known gotcha.

## Build Sequence (suggested for the implementation plan)

1. Identity + `ApplicationUser` + `NookContext` → `IdentityDbContext`; auth wiring in `Program.cs`.
2. `UserId` on `Item`/`Tag`, `ActivityLog` entity, the EF migration, `DbSeeder` demo user.
3. `ICurrentUser`; scope `ItemService`/`TagService`; add `IActivityService` + logging hooks.
4. Login, Registration, Homepage pages + route protection + logout.
5. Nav menu: tags group, new links, account section.
6. Activity Log page.
7. `ITimelineService` + Timeline page (events, then shoutouts).
8. `IAnalyticsService` + Analytics page.
9. Tests throughout; final pass loading every page.
