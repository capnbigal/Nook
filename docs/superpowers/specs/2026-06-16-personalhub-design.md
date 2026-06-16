# Nook — Design (2026-06-16)

## Summary

A personal productivity / knowledge app. All captured information (notes, reminders, bookmarks,
thoughts, lists, todos, ideas, references) is stored as one `Item` entity distinguished by
`ItemType`. Built on .NET 10 Blazor Interactive Server + MudBlazor + EF Core + SQL Server.

## Decisions (confirmed with user)

- **Database:** SQL Server default instance — `Server=.;Database=Nook;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True` (Windows auth, no secrets committed; lives in `appsettings.Development.json`).
- **Auth:** None for v1 (single-user). Identity + per-user ownership deferred.
- **Schema:** EF Core migrations, applied automatically at startup in Development.

## Architecture

- Single Blazor Web App project, **global Interactive Server** render mode, no custom JavaScript.
- **Models:** `Item`, `Tag`, `ItemTag` (join), `ItemLink`. `ItemType` / `ItemStatus` / `Priority`
  are C# enums stored as strings (`HasConversion<string>()`) to match the `nvarchar(50)` schema.
- **Data:** `NookContext` registered via `AddDbContextFactory` (correct pattern for Blazor
  Server — short-lived contexts, safe across concurrent renders). Fluent config: unique `Tag.Name`,
  composite `ItemTag` key, `DeleteBehavior.Restrict` on the self-reference and both `ItemLink`
  FKs (avoids multiple-cascade-path errors), `sysutcdatetime()` defaults, `UpdatedAt` auto-stamped
  in `SaveChanges`. `DbSeeder` migrates + seeds starter data in Development.
- **Services:** `IItemService`/`ItemService` (CRUD, archive, pin/favorite, complete/reopen,
  related-by-tags, children, links, dashboard/reminder/todo queries) and `ITagService`/`TagService`
  (CRUD, get-or-create, assign/remove, summary). `ItemFilter` view model drives list queries
  (search matches Title/Body/Url/tag names).
- **UI:** `MainLayout` (MudAppBar with global search + theme toggle, MudDrawer + MudNavMenu).
  Shared components: `ItemCard`, `ItemEditor`, `ItemFilterBar`, `TagAutocomplete` (pick existing
  or create inline), `TagChips`, `DashboardSection`.
- **Pages:** `/dashboard` (also `/`), `/items`, `/items/new`, `/items/{id:int}`, `/todos`,
  `/reminders`, `/bookmarks`, `/tags`, `/archive`.

## Acceptance criteria — all met

Create any item type · assign tags · search & filter · separate Todo/Reminder/Bookmark views ·
related items by shared tags · archive instead of delete · clean MudBlazor UI · clean build.

## Implementation notes

- Built directly per the user's explicit step-by-step instructions (rather than the formal
  writing-plans / executing-plans flow), since the user supplied a complete spec and asked to build.
- MudBlazor 9.5.0 specifics encountered: dialogs use `ShowMessageBoxAsync` (not `ShowMessageBox`);
  `MudTextField` has no `AutoGrow`; generic components need explicit `T` (`MudChip<T>`,
  `MudSwitch T="bool"`, `MudList`/`MudListItem`); pass HTML attributes (`title`, `aria-label`)
  lowercase to satisfy the MUD0002 analyzer.

## Future improvements

Authentication + multi-user (`UserId` on `Item`); tag rename/recolor; parent picker in the editor;
full-text search and paging.
