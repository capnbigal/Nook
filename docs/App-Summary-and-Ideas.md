# App Summary and Ideas

> Review date: 2026-07-02. This is a read-only review of the Nook repository. No code, config, or data was changed.

## What This App Does

**Nook** is a personal productivity and knowledge-capture web app. It gives one person a single, calm place to capture and organize everything they want to remember — notes, todos, reminders, bookmarks, thoughts, lists, ideas, and references.

- **Main purpose:** Capture any kind of small information as an "item," organize it with tags, and resurface it when it matters (due dates, reminders, pins, favorites, a dashboard, and a timeline).
- **Who it's for:** Individuals who want a lightweight personal "second brain" / task-and-notes tool. It now supports multiple accounts, so a few people can each keep their own private space, but it is designed around single-person use.
- **The problem it solves:** Instead of juggling separate apps for notes, to-dos, reminders, and bookmarks, everything lives in one unified store and one consistent interface. A single `Item` type covers all eight kinds, so capturing is fast and one search covers everything.
- **What a user can currently do:**
  - Register, sign in, sign out, and reset a forgotten password.
  - Create, edit, archive/unarchive, and delete items of any type.
  - Tag items (with colors), filter and search across everything.
  - Mark todos/reminders complete or reopen them; pin and favorite items.
  - Link related items together, nest sub-items under a parent, and see tag-based "related items" suggestions.
  - Browse curated views: Dashboard, All Items, Todos, Reminders, Bookmarks, Tags, Archive.
  - Review their own history and progress: an Activity Log, a Timeline (with weekly "shoutouts"), and an Analytics page with charts.

## Main Features

| Feature | What it does | Main page / route | Data & services | State |
|---|---|---|---|---|
| **Item capture (unified)** | One `Item` entity covers Note, Reminder, Bookmark, Thought, List, Todo, Idea, Reference. Create/edit via a shared editor. | `/items/new`, `ItemEditor.razor` | `Item` table; `IItemService` | Complete |
| **Item detail & actions** | Full detail view: edit, pin, favorite, archive, delete, plus links, sub-items, and related items. | `/items/{id}`, `ItemDetail.razor` | `Item`, `ItemLink`; `IItemService`, `ITagService` | Complete |
| **All Items list + filtering** | Grid of item cards with search + filters (type, status, priority, tag, due-soon, overdue, favorites, pinned). | `/items`, `Items.razor`, `ItemFilterBar.razor` | `IItemService`, `ItemFilter` | Complete |
| **Search** | Text search across title, body, URL, and tag names. | Filter bar + app-bar search box | `IItemService.GetItemsAsync` | Complete (substring `LIKE`, no paging) |
| **Tags** | Per-user tags with optional color; create, delete, view items by tag; tag counts in the nav. | `/tags`, `Tags.razor`, `TagChips`, `TagAutocomplete` | `Tag`, `ItemTag`; `ITagService` | Mostly complete — **rename/recolor has a service method but appears to have no UI** (see Attention) |
| **Todos view** | Todo list with show-completed toggle and inline priority/due-date editing. | `/todos`, `Todos.razor` | `IItemService.GetTodosAsync` | Complete |
| **Reminders view** | Splits reminders into overdue vs. upcoming with a complete button. | `/reminders`, `Reminders.razor` | `IItemService` (reminder queries) | Complete as a **view** — but there is no actual notification/alert (see Attention) |
| **Bookmarks view** | Bookmark cards with a tag filter dropdown. | `/bookmarks`, `Bookmarks.razor` | `IItemService`, `ITagService` | Complete |
| **Dashboard** | Curated sections: overdue, due-soon, pinned, favorites, recently created/updated, tag summary. | `/dashboard`, `Dashboard.razor`, `DashboardSection.razor` | `IItemService`, `ITagService` | Complete |
| **Archive** | Archived items with debounced search. | `/archive`, `Archive.razor` | `IItemService` | Complete |
| **Item links & sub-items** | Manual links between items; parent/child nesting; tag-based related suggestions. | `ItemDetail.razor` | `ItemLink`, `Item.ParentItemId`; `IItemService` | Complete |
| **Activity Log** | Audit trail of every change (created/updated/completed/archived/tagged/deleted…). | `/log`, `Log.razor` | `ActivityLog`; `IActivityService` | Complete |
| **Timeline** | Day-grouped history with per-week "shoutouts" (e.g. "5 items completed 🎉"). | `/timeline`, `Timeline.razor` | `ActivityLog`; `ITimelineService` | Complete (reads last 500 events — silent cap) |
| **Analytics** | Stat cards + charts: by type, by status, weekly trend, top tags, completion rate, busiest day. | `/analytics`, `Analytics.razor` | `IAnalyticsService` | Complete |
| **Authentication** | Register / login / logout with ASP.NET Core Identity; per-user data scoping. | `/register`, `/login`, `/Account/Logout` | Identity tables; `ICurrentUser` | Complete |
| **Password reset** | Forgot-password generates a reset token; reset page sets a new password. | `/forgot-password`, `/reset-password` | `UserManager` tokens | Complete, but **email isn't wired up — the reset link is shown on-screen** (see Attention) |
| **Theme toggle** | Light/dark mode in the app bar. | `MainLayout.razor` | — | Complete |
| **Seed / demo data** | On an empty DB, creates a demo user + sample items/tags. | `DbSeeder.cs` | all tables | Complete |

## How the App Works

**Navigation & pages.** After signing in, the user lands on the **Dashboard**. The left nav (`NavMenu.razor`) links to Dashboard, All Items, Todos, Reminders, Bookmarks, then Timeline / Analytics / Activity Log / Archive, a live list of Tags with counts, and a "New Item" shortcut. The top app bar (`MainLayout.razor`) has a global search box, an add button, and the dark-mode toggle. Signed-out visitors see a marketing-style landing page (`Home.razor`) with "Get started" / "Sign in".

**Typical flow.** Open the app → (register or sign in) → Dashboard shows what needs attention → capture something via "New Item" (pick a type, add title/body/tags/dates) → it appears in the relevant views → mark todos/reminders done, pin/favorite, tag, or link items → review progress on the Timeline and Analytics pages.

**How data is saved and retrieved.** The app is **.NET 10 Blazor (Interactive Server)** with **MudBlazor** for UI and **EF Core** against **SQL Server**. All data access goes through a small service layer (`ItemService`, `TagService`, `ActivityService`, `TimelineService`, `AnalyticsService`) that each open a short-lived `NookContext` via `IDbContextFactory` (the safe pattern for Blazor Server concurrency). Every read and write is scoped to the current user's `UserId`, resolved from the auth cookie by `CurrentUser`/`ICurrentUser`. Write operations also append an `ActivityLog` row, which feeds the Log, Timeline, and parts of Analytics. Timestamps (`CreatedAt`/`UpdatedAt`) are stamped automatically in `NookContext.SaveChanges`.

**Login / auth / settings.** ASP.NET Core Identity provides register/login/logout and password reset. Password rules are relaxed (min length 6, no uppercase/symbol required) and email confirmation is off. Auth pages render as static server-side pages so the login cookie can be written; the rest of the app is interactive. There is no user "settings" page beyond the theme toggle.

**External integrations / background jobs.** There are **none** — no email sending, no scheduled jobs, no external APIs, no import/export. "Reminders" are simply items with a `ReminderDate`; nothing actively notifies the user. Deployment (see `deployment/`) packages the app as a Docker image published to GHCR and run behind nginx on a shared droplet; there's a GitHub Actions build. (Server IP, domain, and container details are in `deployment/DEPLOY.md` and are intentionally not reproduced here.)

## Project Structure

Single web project **`Nook.csproj`** (solution `Nook.sln`) plus a test project **`Nook.Tests`** (xUnit + EF InMemory). Namespaces are `Nook.*`; the DbContext is `NookContext`.

- **Entry point:** `Program.cs` — service registration (Identity, MudBlazor, DbContext factory, app services), request pipeline, and startup migration+seed via `DbSeeder.InitializeAsync`.
- **Models (`Models/`):** `Item.cs` (the unified entity), `Tag.cs`, `ItemTag.cs` (join), `ItemLink.cs`, `ActivityLog.cs`, `ApplicationUser.cs`, `Enums.cs` (`ItemType`, `ItemStatus`, `Priority`, `ActivityType`).
- **Data (`Data/`):** `NookContext.cs` (model config, FK/cascade rules, timestamp stamping), `DbSeeder.cs`, `Migrations/`.
- **Services (`Services/`):** business logic and data access — `ItemService`/`IItemService`, `TagService`/`ITagService`, `ActivityService`, `TimelineService`, `AnalyticsService`, `CurrentUser`/`ICurrentUser`, plus `ItemFilter.cs` and the model records in `AnalyticsModels.cs` / `TimelineModels.cs`.
- **Components (`Components/`):**
  - `Pages/` — the routable screens (Dashboard, Items, NewItem, ItemDetail, Todos, Reminders, Bookmarks, Tags, Archive, Analytics, Timeline, Log, Home, Error, NotFound).
  - `Shared/` — reusable UI: `ItemEditor`, `ItemCard`, `ItemFilterBar`, `TagAutocomplete`, `TagChips`, `DashboardSection`.
  - `Layout/` — `MainLayout`, `NavMenu`, `AuthLayout`, `ReconnectModal`.
  - `Account/` — Identity plumbing and the `Login`, `Register`, `ForgotPassword`, `ResetPassword`, `Logout` pages.
  - `App.razor`, `Routes.razor`, `_Imports.razor` — app shell and render-mode gating.
- **Configuration:** `appsettings.json`, `appsettings.Development.json` (dev connection string — local Windows-auth, no password), `Properties/launchSettings.json`, `.github/copilot-instructions.md`, `deployment/` (`DEPLOY.md`, `nginx-nook.conf`, `.env.template`).
- **Existing documentation:** `README.md`; design specs and plans under `docs/superpowers/specs/` and `docs/superpowers/plans/` (PersonalHub design, users-and-insights, password-reset); `deployment/DEPLOY.md`.

**Where to look when changing a feature:** the page in `Components/Pages/` for UI/behavior, the matching `Services/*Service.cs` for data logic, `Models/` + `Data/NookContext.cs` for schema (then add an EF migration).

## Things That May Need Attention

### Confirmed issues

- **`README.md` is out of date.** It still says "v1 is single-user with no authentication," omits the Timeline/Analytics/Activity-Log/auth/password-reset features, and lists an older project structure. This is the most visible drift and the easiest to fix.
- **Password reset has no email delivery.** `ForgotPassword.razor` generates a token and **displays the reset link directly on the page** ("Email isn't configured, so use this link…"). This works for a personal/demo app but means anyone who can reach the forgot-password page for a known email gets a working reset link on screen.
- **Account/email enumeration.** `ForgotPassword` says "No account found with that email," and the reset/login errors reveal whether an account exists. Standard practice is a generic "if that email exists, we sent a link" message.
- **Demo account with a known password ships enabled.** `DbSeeder` seeds `demo@nook.local` / `Demo123!` on any empty database, including the public deployment. `DEPLOY.md` notes this and says to remove it if undesired — but by default it's live.
- **Relaxed auth security settings.** In `Program.cs`: password min length 6 with no complexity, `RequireConfirmedAccount = false`. Reasonable for personal use; worth a conscious decision before wider exposure.
- **No paging anywhere.** `GetItemsAsync` and the list pages load all matching items; Analytics loads all of a user's items into memory; Timeline caps at the **last 500 events silently**. Fine at small scale, but there's no empty-vs-truncated signal to the user.
- **Dev connection string is committed** (`appsettings.Development.json`). It's a local `Server=.;Trusted_Connection=True` string with **no password**, so low sensitivity, and production is injected via env — but it's still a checked-in infrastructure detail.

### Assumptions / things to verify

- **Tag rename/recolor likely has no UI.** `TagService.UpdateAsync` exists and supports it, but the Tags page appears to offer only create / delete / view-items. If so, the service capability is unreachable from the UI. (Assumption — based on the page review, not a full read of `Tags.razor`.)
- **"Reminders" don't remind.** There's no background job or notification; reminders are just filtered views. This is a design gap rather than a bug, but the name may set an expectation the app doesn't meet.
- **Search case-sensitivity** depends on the SQL Server collation (default is case-insensitive, so probably fine — not independently verified here).
- **Lists as a type look under-developed.** `ItemType.List` exists and parent/child nesting is supported, but there's no dedicated list-building UI beyond generic sub-items. (Assumption.)

## Feature Ideas

### Easy Improvements

- **Update `README.md` to match reality.** *Why:* the current README actively misleads about auth and features. *Effort:* Small. *Priority:* High.
- **Generic password-reset / login messages (no account enumeration).** *Why:* small, standard privacy hardening. *Effort:* Small. *Priority:* Medium.
- **Add a Tag edit (rename/recolor) UI.** *Why:* the service already supports it; exposing it removes a dead-end where a typo'd tag can only be deleted. *Effort:* Small. *Priority:* Medium.
- **Show "showing N of M / results truncated" and empty-state hints on long lists.** *Why:* users can't currently tell when a view is capped (esp. Timeline's 500-event limit). *Effort:* Small. *Priority:* Medium.
- **Gate or flag the demo user on the public deployment.** *Why:* a known-password account is publicly reachable by default. *Effort:* Small. *Priority:* Medium.
- **Add a copy-to-clipboard button for the on-screen reset link.** *Why:* until email is wired up, it's the only path; make it painless. *Effort:* Small. *Priority:* Low.

### Useful Next Features

- **Real reminder notifications** (browser/toast on login for due reminders, or email). *Why:* makes "Reminders" actually remind — the biggest gap between name and behavior. *Effort:* Medium (Large if email/push). *Priority:* High.
- **Paging or infinite scroll on All Items / Archive / Log.** *Why:* keeps the app fast and honest as data grows. *Effort:* Medium. *Priority:* Medium.
- **Data export/import (JSON or Markdown).** *Why:* personal data ownership and backup; also enables migrating in existing notes. *Effort:* Medium. *Priority:* Medium.
- **Dedicated checklist/List builder** for `ItemType.List`. *Why:* lists are a first-class type but lack a purpose-built editing experience. *Effort:* Medium. *Priority:* Medium.
- **Quick-capture box on the Dashboard** (title + type, one field). *Why:* speeds the core "capture fast" promise from the landing page. *Effort:* Small–Medium. *Priority:* Medium.
- **Bulk actions** (multi-select to tag/archive/delete). *Why:* tidying up many items is currently one-at-a-time. *Effort:* Medium. *Priority:* Low.

### Bigger Future Ideas

- **Email integration** (reset links, reminder digests) via SMTP/SendGrid. *Why:* completes password reset and unlocks notifications. *Effort:* Large. *Priority:* Medium.
- **Full-text search** (SQL Server FTS) with ranking and highlighting. *Why:* substring `LIKE` won't scale or rank well. *Effort:* Large. *Priority:* Low.
- **Recurring items / reminders.** *Why:* common personal-productivity need (weekly chores, monthly bills). *Effort:* Large. *Priority:* Low.
- **Markdown rendering for item bodies.** *Why:* richer notes without a heavy editor. *Effort:* Medium. *Priority:* Low.
- **Mobile-friendly PWA / offline capture.** *Why:* capture-on-the-go is where a lot of personal notes originate. *Effort:* Large. *Priority:* Low.

## Recommended Next Steps

Ranked for usefulness × simplicity × maintainability:

1. **Rewrite `README.md`** to reflect auth, insights pages, and the current structure. (Small, High)
2. **Decide the demo-account policy** for the public deployment — disable the seed there or document it clearly. (Small, security-relevant)
3. **Add Tag rename/recolor UI** to surface the existing service method. (Small)
4. **Add truncation/empty-state indicators** on capped lists (Items, Log, Timeline). (Small)
5. **Make login/reset messages non-enumerating.** (Small)
6. **Implement reminder notifications** (start with an at-login "due reminders" toast before tackling email). (Medium, highest user value)
7. **Add paging** to All Items / Archive / Log. (Medium)
8. **Add JSON export/import** for backup and peace of mind. (Medium)
9. **Build a dedicated List/checklist experience.** (Medium)
10. **Revisit password policy / email confirmation** as a conscious choice if the app will be shared more widely. (Small decision, possibly Large to fully implement email confirmation)

## Questions or Unknowns

- **Who is the intended audience going forward** — just you, or a small group of invited users? This changes how much the auth-hardening and email items matter.
- **Is public exposure intended long-term?** The deployment targets a public subdomain; if so, the demo account and enumeration items rise in priority.
- **Should email be wired up?** Several ideas (reset links, reminder digests) depend on whether you want to run/pay for an email service.
- **Is the Tag rename/recolor UI genuinely missing**, or did the review miss it? Worth confirming before building it.
- **How large is your real dataset likely to get?** That decides whether paging/full-text search is worth doing soon or can wait.
- **Are "Lists" and "Thoughts" meant to behave differently from Notes**, or are they mostly organizational labels today?
- **Any intent to keep the `docs/superpowers/` specs and plans** as living documentation, or are they historical? (They currently describe past work, not the current backlog.)
</content>
</invoke>
