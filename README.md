# Nook

A personal productivity / knowledge app built with **.NET 10 Blazor (Interactive Server)**,
**MudBlazor**, **EF Core**, and **SQL Server**. Notes, reminders, bookmarks, thoughts, lists,
todos, ideas and references are all stored as a single unified `Item` entity, distinguished by
an `ItemType`.

## Features

- Create, edit, archive and delete items of any type
- Tags with colors, assigned to many items (MudBlazor chips), filterable
- Search across title, body, URL and tag names; filter by type, status, priority, due-soon,
  overdue, favorites, pinned, and active/archived
- Related items: shared-tag suggestions, parent/child sub-items, and manual links
- Dedicated views: Dashboard, Todos, Reminders, Bookmarks, Tags, Archive
- Light/dark theme toggle, no custom JavaScript

## Prerequisites

- .NET 10 SDK
- SQL Server (the dev connection string targets the local default instance with Windows auth)

## Configuration

The connection string lives in `appsettings.Development.json` (no secrets committed):

```json
"ConnectionStrings": {
  "DefaultConnection": "Server=.;Database=Nook;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True"
}
```

Change `Server=.` to e.g. `(localdb)\\MSSQLLocalDB` or `.\\MSSQLSERVER01` to target a different
instance. For production, supply the connection string via environment variables or user-secrets
rather than committing it.

## Running

```bash
dotnet run
```

On first run in Development the app applies migrations and seeds a few sample items/tags, then
listens on the URL printed in the console (e.g. http://localhost:5176).

## Database / migrations

The schema is managed with EF Core migrations (`Data/Migrations/`). It is applied automatically
at startup in Development. To manage it manually:

```bash
# create a new migration after changing the model
dotnet ef migrations add <Name> -o Data/Migrations

# apply migrations
dotnet ef database update
```

(The EF tooling reads `appsettings.Development.json`; ensure `ASPNETCORE_ENVIRONMENT=Development`.)

## Project structure

```
Models/         Item, Tag, ItemTag, ItemLink + enums (ItemType, ItemStatus, Priority)
Data/           NookContext (DbContextFactory), DbSeeder, Migrations/
Services/       IItemService/ItemService, ITagService/TagService, ItemFilter
Components/
  Layout/       MainLayout (AppBar + Drawer), NavMenu
  Shared/       ItemCard, ItemEditor, ItemFilterBar, TagAutocomplete, TagChips, DashboardSection
  Pages/        Dashboard, Items, NewItem, ItemDetail, Todos, Reminders, Bookmarks, Tags, Archive
```

## Notes / future improvements

- v1 is single-user with no authentication. Add ASP.NET Core Identity and a `UserId` on `Item`
  to support multiple users.
- Tag rename/recolor and richer list-item (`ParentItemId`) management are good next steps.
- Consider full-text search and paging for large datasets.
