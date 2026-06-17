# Nook Multi-User, Auth, Timeline, Analytics & Activity Log — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn Nook from a single-user, no-auth app into a multi-user app with ASP.NET Core Identity, per-user data isolation, an activity audit log, and four new surfaces (Homepage, Timeline with shoutouts, Analytics, Log) plus a tags section in the nav menu.

**Architecture:** Identity is added to the existing `NookContext` (it becomes `IdentityDbContext<ApplicationUser>`). Every `Item` and `Tag` gains a `UserId`. A service-layer `ICurrentUser` resolves the signed-in user from the cascaded `AuthenticationState`, and all `ItemService`/`TagService` queries filter by it. An `ActivityLog` table is written on every item mutation and feeds both the Log page and the Timeline. New Razor pages render under the existing `MainLayout` for authenticated users; Home/Login/Register render static under a minimal auth layout using the standard Blazor `[ExcludeFromInteractiveRouting]` pattern.

**Tech Stack:** .NET 10, Blazor Web App (Interactive Server, global), MudBlazor 9.5.0, EF Core 10 (SQL Server), ASP.NET Core Identity, xUnit + EF Core InMemory for tests.

## Global Constraints

- **Target framework:** `net10.0`. All EF/Identity packages pinned to `10.0.9` (match the existing EF packages).
- **MudBlazor 9.5.0 API rules:** dialogs use `ShowMessageBoxAsync`; generic components need explicit `T` (`MudChip<T>`, `MudSwitch T="bool"`, `MudList`/`MudListItem`); HTML passthrough attributes (`title`, `aria-label`) must be lowercase or analyzer MUD0002 warns; `MudTextField` has no `AutoGrow`.
- **EF Core translation gotcha:** never `OrderBy` a property of a projected record. Order on the source expression *before* `.Select(...)`. This only surfaces when the page runs the query (prerender) — verify pages by loading them, not just by booting.
- **DbContext access:** always via `IDbContextFactory<NookContext>` and a short-lived `await using var db = await _factory.CreateDbContextAsync();` — never a long-lived context.
- **Namespaces:** `Nook.Models`, `Nook.Data`, `Nook.Services`, `Nook.Components.*`. Enums stored as strings via `.HasConversion<string>()`.
- **Run/verify:** `dotnet build` to compile; `dotnet watch` (not `dotnet run`) to open a browser. App URL is http://localhost:5176. Migrations auto-apply at startup in Development via `DbSeeder`.
- **Every mutating `ItemService` operation must write exactly one `ActivityLog` row.** Tags are per-user. Registration is open with no email confirmation.

---

## Task 1: Add Identity packages, `ApplicationUser`, and make `NookContext` an `IdentityDbContext`

**Files:**
- Modify: `Nook.csproj` (add packages)
- Create: `Models/ApplicationUser.cs`
- Modify: `Data/NookContext.cs:12-26`

**Interfaces:**
- Produces: `Nook.Models.ApplicationUser : IdentityUser` (string `Id`). `NookContext : IdentityDbContext<ApplicationUser>`.

- [ ] **Step 1: Add the Identity package reference**

In `Nook.csproj`, add inside the existing `<ItemGroup>`:

```xml
    <PackageReference Include="Microsoft.AspNetCore.Identity.EntityFrameworkCore" Version="10.0.9" />
```

- [ ] **Step 2: Create `ApplicationUser`**

Create `Models/ApplicationUser.cs`:

```csharp
using Microsoft.AspNetCore.Identity;

namespace Nook.Models;

/// <summary>The application user. Owns Items, Tags and ActivityLog rows.</summary>
public class ApplicationUser : IdentityUser
{
    // IdentityUser already provides Id (string), UserName, Email, PasswordHash, etc.
}
```

- [ ] **Step 3: Make `NookContext` inherit `IdentityDbContext<ApplicationUser>`**

In `Data/NookContext.cs`, change the using block and class declaration:

```csharp
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Nook.Models;

namespace Nook.Data;

public class NookContext : IdentityDbContext<ApplicationUser>
{
    public NookContext(DbContextOptions<NookContext> options)
        : base(options)
    {
    }
```

Leave the rest of the file unchanged for now (the existing `base.OnModelCreating(modelBuilder)` call at the top of `OnModelCreating` is already present and is required — keep it first).

- [ ] **Step 4: Build to verify it compiles**

Run: `dotnet build`
Expected: Build succeeds. (The app will NOT run correctly yet — the model changed but there's no migration. That's Task 3.)

- [ ] **Step 5: Commit**

```bash
git add Nook.csproj Models/ApplicationUser.cs Data/NookContext.cs
git commit -m "feat: add ASP.NET Core Identity and ApplicationUser"
```

---

## Task 2: Add `UserId` to `Item`/`Tag`, add `ActivityLog` entity, and configure the model

**Files:**
- Modify: `Models/Item.cs:11-58`
- Modify: `Models/Tag.cs`
- Modify: `Models/Enums.cs` (add `ActivityType`)
- Create: `Models/ActivityLog.cs`
- Modify: `Data/NookContext.cs` (add `DbSet`, configure new entities, per-user tag uniqueness)

**Interfaces:**
- Produces: `Item.UserId` (string), `Tag.UserId` (string), `ActivityLog` entity, `ActivityType` enum `{ Created, Updated, Completed, Reopened, Archived, Unarchived, Tagged, Deleted }`, `NookContext.ActivityLogs` DbSet.

- [ ] **Step 1: Add `UserId` to `Item`**

In `Models/Item.cs`, add after the `ItemId` property (line ~13):

```csharp
    /// <summary>Owner of this item. FK to ApplicationUser.</summary>
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }
```

- [ ] **Step 2: Add `UserId` to `Tag`**

In `Models/Tag.cs`, add after the `TagId` property:

```csharp
    /// <summary>Owner of this tag. Tags are per-user. FK to ApplicationUser.</summary>
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }
```

- [ ] **Step 3: Add the `ActivityType` enum**

In `Models/Enums.cs`, append:

```csharp
/// <summary>The kind of change recorded in the activity log. Stored as a string.</summary>
public enum ActivityType
{
    Created,
    Updated,
    Completed,
    Reopened,
    Archived,
    Unarchived,
    Tagged,
    Deleted
}
```

- [ ] **Step 4: Create the `ActivityLog` entity**

Create `Models/ActivityLog.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace Nook.Models;

/// <summary>
/// An immutable audit record of a change to an item. Feeds the Log page and the
/// Timeline. ItemId is nullable and ItemTitle is denormalized so a log row
/// survives deletion of its item.
/// </summary>
public class ActivityLog
{
    public int ActivityLogId { get; set; }

    /// <summary>Owner. FK to ApplicationUser.</summary>
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    /// <summary>The affected item, or null if it has since been deleted.</summary>
    public int? ItemId { get; set; }

    /// <summary>Snapshot of the item's title at the time of the event.</summary>
    [MaxLength(300)]
    public string ItemTitle { get; set; } = string.Empty;

    public ActivityType Type { get; set; }

    public DateTime Timestamp { get; set; }

    /// <summary>Optional human-readable detail, e.g. "status Open → Done".</summary>
    [MaxLength(500)]
    public string? Detail { get; set; }
}
```

- [ ] **Step 5: Configure the new model in `NookContext`**

In `Data/NookContext.cs`, add a `DbSet`:

```csharp
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();
```

In `OnModelCreating`, change the `Tag` configuration's unique index from global to per-user, and add `Item.UserId` + `ActivityLog` config. Replace the `Tag` block and add the new blocks:

```csharp
        modelBuilder.Entity<Item>(entity =>
        {
            // ... existing Item config stays unchanged ...
            entity.Property(e => e.UserId).IsRequired();
            entity.HasIndex(e => e.UserId);
        });

        modelBuilder.Entity<Tag>(entity =>
        {
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Color).HasMaxLength(50);
            entity.Property(e => e.UserId).IsRequired();
            // Tag names are unique PER USER, not globally.
            entity.HasIndex(e => new { e.UserId, e.Name }).IsUnique();
        });

        modelBuilder.Entity<ActivityLog>(entity =>
        {
            entity.Property(e => e.Type).HasConversion<string>().HasMaxLength(50).IsRequired();
            entity.Property(e => e.ItemTitle).HasMaxLength(300).IsRequired();
            entity.Property(e => e.Detail).HasMaxLength(500);
            entity.HasIndex(e => new { e.UserId, e.Timestamp });
        });
```

> Add the two new `Item.UserId` lines inside the *existing* `Item` entity block rather than duplicating it. Leave the `ItemTag` and `ItemLink` blocks unchanged.

- [ ] **Step 6: Build to verify it compiles**

Run: `dotnet build`
Expected: Build succeeds.

- [ ] **Step 7: Commit**

```bash
git add Models/Item.cs Models/Tag.cs Models/Enums.cs Models/ActivityLog.cs Data/NookContext.cs
git commit -m "feat: add UserId to Item/Tag and ActivityLog entity"
```

---

## Task 3: Create the EF migration and seed a demo user

**Files:**
- Create: `Data/Migrations/<timestamp>_AddIdentityAndActivityLog.cs` (generated)
- Modify: `Data/DbSeeder.cs`

**Interfaces:**
- Consumes: model from Tasks 1–2.
- Produces: a migration covering Identity tables + `UserId` columns + `ActivityLog`; `DbSeeder` assigns all seed data to a demo user `demo@nook.local`.

- [ ] **Step 1: Ensure the EF CLI is available**

Run: `dotnet ef --version`
Expected: prints a 10.x version. If "command not found", run:
`dotnet tool install --global dotnet-ef --version 10.0.9`

- [ ] **Step 2: Add the migration**

Run: `dotnet ef migrations add AddIdentityAndActivityLog`
Expected: a new migration appears under `Data/Migrations/`. Open it and confirm it creates the `AspNetUsers`/`AspNetRoles`/etc. tables, adds `UserId` to `Items` and `Tags`, and creates `ActivityLogs`.

- [ ] **Step 3: Rewrite `DbSeeder` to create and own data with a demo user**

Replace `Data/DbSeeder.cs` with:

```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Nook.Models;

namespace Nook.Data;

/// <summary>
/// Applies pending migrations and, on an empty database, creates a demo user
/// (demo@nook.local / Demo123!) and seeds starter items/tags owned by them.
/// </summary>
public static class DbSeeder
{
    public const string DemoEmail = "demo@nook.local";
    public const string DemoPassword = "Demo123!";

    public static async Task InitializeAsync(IServiceProvider services)
    {
        var factory = services.GetRequiredService<IDbContextFactory<NookContext>>();
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.MigrateAsync();

        if (await db.Tags.AnyAsync() || await db.Items.AnyAsync())
        {
            return;
        }

        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var demo = await userManager.FindByEmailAsync(DemoEmail);
        if (demo is null)
        {
            demo = new ApplicationUser
            {
                UserName = DemoEmail,
                Email = DemoEmail,
                EmailConfirmed = true
            };
            var result = await userManager.CreateAsync(demo, DemoPassword);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    "Failed to create demo user: " +
                    string.Join("; ", result.Errors.Select(e => e.Description)));
            }
        }

        var uid = demo.Id;
        var work = new Tag { Name = "work", Color = "#1E88E5", UserId = uid };
        var personal = new Tag { Name = "personal", Color = "#43A047", UserId = uid };
        var ideas = new Tag { Name = "ideas", Color = "#8E24AA", UserId = uid };
        db.Tags.AddRange(work, personal, ideas);

        db.Items.AddRange(
            new Item
            {
                Title = "Welcome to Nook",
                Body = "This is a sample note. Capture notes, todos, reminders, bookmarks, "
                     + "thoughts, ideas and lists — everything is an \"item\". Use the menu on "
                     + "the left to explore, and the + button to create your own.",
                ItemType = ItemType.Note,
                Status = ItemStatus.Open,
                IsPinned = true,
                UserId = uid,
                ItemTags = new List<ItemTag> { new() { Tag = personal } }
            },
            new Item
            {
                Title = "Try completing this todo",
                Body = "Open the Todos page and mark this done.",
                ItemType = ItemType.Todo,
                Status = ItemStatus.Open,
                Priority = Priority.Medium,
                DueDate = DateTime.UtcNow.AddDays(2),
                UserId = uid,
                ItemTags = new List<ItemTag> { new() { Tag = work } }
            },
            new Item
            {
                Title = "MudBlazor documentation",
                ItemType = ItemType.Bookmark,
                Status = ItemStatus.Open,
                Url = "https://mudblazor.com",
                UserId = uid,
                ItemTags = new List<ItemTag> { new() { Tag = ideas } }
            }
        );

        await db.SaveChangesAsync();
    }
}
```

> `DbSeeder` now needs `UserManager<ApplicationUser>`, which is registered in Task 4. Until Task 4 is done the app will throw at startup if seeding runs on an empty DB. Run the app for the first time only after Task 4. Build still succeeds now.

- [ ] **Step 4: Build**

Run: `dotnet build`
Expected: Build succeeds.

- [ ] **Step 5: Commit**

```bash
git add Data/Migrations Data/DbSeeder.cs
git commit -m "feat: migration for identity/activity log and demo-user seeding"
```

---

## Task 4: Wire up authentication, render-mode gating, and the auth layout

**Files:**
- Modify: `Program.cs`
- Create: `Components/Account/IdentityRevalidatingAuthenticationStateProvider.cs`
- Create: `Components/Account/IdentityUserAccessor.cs`
- Create: `Components/Account/IdentityRedirectManager.cs`
- Create: `Components/Account/IdentityComponentsEndpointRouteBuilderExtensions.cs`
- Create: `Components/Account/Shared/RedirectToLogin.razor`
- Create: `Components/Layout/AuthLayout.razor`
- Modify: `Components/App.razor`
- Modify: `Components/Routes.razor`
- Modify: `Components/_Imports.razor`

**Interfaces:**
- Produces: configured Identity services; `IdentityRedirectManager.RedirectTo(...)`; `IdentityUserAccessor.GetRequiredUserAsync(HttpContext)`; `MapAdditionalIdentityEndpoints()`; static-rendered Account pages via `[ExcludeFromInteractiveRouting]`; `[Authorize]` default on all routable components.

- [ ] **Step 1: Add the Identity service helper files**

Create `Components/Account/IdentityRevalidatingAuthenticationStateProvider.cs`:

```csharp
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Nook.Models;
using System.Security.Claims;

namespace Nook.Components.Account;

/// <summary>
/// Server-side AuthenticationStateProvider that periodically revalidates the
/// signed-in user's security stamp against the database.
/// </summary>
internal sealed class IdentityRevalidatingAuthenticationStateProvider(
    ILoggerFactory loggerFactory,
    IServiceScopeFactory scopeFactory,
    IOptions<IdentityOptions> options)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(30);

    protected override async Task<bool> ValidateAuthenticationStateAsync(
        AuthenticationState authenticationState, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await ValidateSecurityStampAsync(userManager, authenticationState.User);
    }

    private async Task<bool> ValidateSecurityStampAsync(
        UserManager<ApplicationUser> userManager, ClaimsPrincipal principal)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return false;
        }
        if (!userManager.SupportsUserSecurityStamp)
        {
            return true;
        }
        var principalStamp = principal.FindFirstValue(options.Value.ClaimsIdentity.SecurityStampClaimType);
        var userStamp = await userManager.GetSecurityStampAsync(user);
        return principalStamp == userStamp;
    }
}
```

Create `Components/Account/IdentityUserAccessor.cs`:

```csharp
using Microsoft.AspNetCore.Identity;
using Nook.Models;

namespace Nook.Components.Account;

internal sealed class IdentityUserAccessor(
    UserManager<ApplicationUser> userManager, IdentityRedirectManager redirectManager)
{
    public async Task<ApplicationUser> GetRequiredUserAsync(HttpContext context)
    {
        var user = await userManager.GetUserAsync(context.User);
        if (user is null)
        {
            redirectManager.RedirectToWithStatus(
                "Account/InvalidUser",
                $"Error: Unable to load user with ID '{userManager.GetUserId(context.User)}'.",
                context);
        }
        return user;
    }
}
```

Create `Components/Account/IdentityRedirectManager.cs`:

```csharp
using Microsoft.AspNetCore.Components;
using System.Diagnostics.CodeAnalysis;

namespace Nook.Components.Account;

internal sealed class IdentityRedirectManager(NavigationManager navigationManager)
{
    public const string StatusCookieName = "Identity.StatusMessage";

    [DoesNotReturn]
    public void RedirectTo(string? uri)
    {
        uri ??= "";
        if (!Uri.IsWellFormedUriString(uri, UriKind.Relative))
        {
            uri = navigationManager.ToBaseRelativePath(uri);
        }
        navigationManager.NavigateTo(uri);
        throw new InvalidOperationException(
            $"{nameof(IdentityRedirectManager)} can only be used during static rendering.");
    }

    [DoesNotReturn]
    public void RedirectTo(string uri, Dictionary<string, object?> queryParameters)
    {
        var uriWithoutQuery = navigationManager.ToAbsoluteUri(uri).GetLeftPart(UriPartial.Path);
        var newUri = navigationManager.GetUriWithQueryParameters(uriWithoutQuery, queryParameters);
        RedirectTo(newUri);
    }

    [DoesNotReturn]
    public void RedirectToWithStatus(string uri, string message, HttpContext context)
    {
        context.Response.Cookies.Append(StatusCookieName, message,
            new CookieOptions { MaxAge = TimeSpan.FromSeconds(5), HttpOnly = true, IsEssential = true });
        RedirectTo(uri);
    }

    [DoesNotReturn]
    public void RedirectToCurrentPage() => RedirectTo("/");
}
```

Create `Components/Account/IdentityComponentsEndpointRouteBuilderExtensions.cs`:

```csharp
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Nook.Models;

namespace Microsoft.AspNetCore.Routing;

internal static class IdentityComponentsEndpointRouteBuilderExtensions
{
    public static IEndpointConventionBuilder MapAdditionalIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var group = endpoints.MapGroup("/Account");

        group.MapPost("/Logout", async (
            SignInManager<ApplicationUser> signInManager,
            [Microsoft.AspNetCore.Mvc.FromForm] string returnUrl) =>
        {
            await signInManager.SignOutAsync();
            return TypedResults.LocalRedirect($"~/{returnUrl}");
        });

        return group;
    }
}
```

- [ ] **Step 2: Add `RedirectToLogin` and the auth layout**

Create `Components/Account/Shared/RedirectToLogin.razor`:

```razor
@inject NavigationManager Nav

@code {
    protected override void OnInitialized()
    {
        var returnUrl = Uri.EscapeDataString(
            Nav.ToBaseRelativePath(Nav.Uri));
        Nav.NavigateTo($"login?returnUrl={returnUrl}", forceLoad: true);
    }
}
```

Create `Components/Layout/AuthLayout.razor` (minimal centered chrome for Home/Login/Register, no nav drawer):

```razor
@inherits LayoutComponentBase

<MudThemeProvider />
<MudPopoverProvider />
<MudDialogProvider />
<MudSnackbarProvider />

<MudLayout>
    <MudMainContent>
        <MudContainer MaxWidth="MaxWidth.Small" Class="mt-16">
            @Body
        </MudContainer>
    </MudMainContent>
</MudLayout>
```

- [ ] **Step 3: Update `Program.cs`**

Add usings at the top:

```csharp
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Nook.Components.Account;
using Nook.Models;
```

After `builder.Services.AddMudServices();` add:

```csharp
// Authentication & Identity.
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityUserAccessor>();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = IdentityConstants.ApplicationScheme;
    options.DefaultSignInScheme = IdentityConstants.ApplicationScheme;
})
.AddIdentityCookies();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
    options.Password.RequiredLength = 6;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<NookContext>()
.AddSignInManager()
.AddDefaultTokenProviders();
```

In the HTTP pipeline, after `app.UseHttpsRedirection();` add:

```csharp
app.UseAuthentication();
app.UseAuthorization();
```

After `app.MapRazorComponents<App>().AddInteractiveServerRenderMode();` add:

```csharp
app.MapAdditionalIdentityEndpoints();
```

- [ ] **Step 4: Gate render mode in `App.razor`**

Replace `Components/App.razor` `<head>`/`<body>` render-mode attributes. Change the `HeadOutlet` and `Routes` lines and add a `@code` block at the end of the file:

```razor
    <HeadOutlet @rendermode="@PageRenderMode" />
</head>

<body>
    <Routes @rendermode="@PageRenderMode" />
    <ReconnectModal />
    <script src="@Assets["_framework/blazor.web.js"]"></script>
    <script src="_content/MudBlazor/MudBlazor.min.js"></script>
</body>

</html>

@code {
    [CascadingParameter] private HttpContext HttpContext { get; set; } = default!;

    private IComponentRenderMode? PageRenderMode =>
        HttpContext.AcceptsInteractiveRouting() ? InteractiveServer : null;
}
```

Add `@using Microsoft.AspNetCore.Components.Endpoints` at the top of `App.razor` (for `AcceptsInteractiveRouting`). `InteractiveServer` is already available via the `RenderMode` static using in `_Imports.razor`.

- [ ] **Step 5: Use `AuthorizeRouteView` in `Routes.razor`**

Replace `Components/Routes.razor`:

```razor
@using Microsoft.AspNetCore.Components.Authorization
@using Nook.Components.Account.Shared

<Router AppAssembly="typeof(Program).Assembly" NotFoundPage="typeof(Pages.NotFound)">
    <Found Context="routeData">
        <AuthorizeRouteView RouteData="routeData" DefaultLayout="typeof(Layout.MainLayout)">
            <NotAuthorized>
                <RedirectToLogin />
            </NotAuthorized>
        </AuthorizeRouteView>
        <FocusOnNavigate RouteData="routeData" Selector="h1" />
    </Found>
</Router>
```

- [ ] **Step 6: Default all routable pages to `[Authorize]`**

In `Components/_Imports.razor`, append:

```razor
@using Microsoft.AspNetCore.Authorization
@using Microsoft.AspNetCore.Components.Authorization
@attribute [Authorize]
```

> This makes every page require auth by default. Home/Login/Register will opt out with `[AllowAnonymous]` in later tasks. `NotFound.razor` and `Error.razor` must also get `[AllowAnonymous]` — do that now:

In `Components/Pages/NotFound.razor` and `Components/Pages/Error.razor`, add near the top (after existing `@page`/attributes):

```razor
@attribute [AllowAnonymous]
```

- [ ] **Step 7: Build**

Run: `dotnet build`
Expected: Build succeeds.

- [ ] **Step 8: First run — verify seeding and the auth gate**

Run: `dotnet watch` (let it open the browser; if the DB has old data from before per-user columns, drop the `Nook` database first so seeding runs fresh).
Expected: App starts. Navigating to `/dashboard` (or any app page) redirects to `/login` (which 404s for now — that page is Task 5). No startup exception (demo user seeded). Stop the app.

- [ ] **Step 9: Commit**

```bash
git add Program.cs Components/Account Components/Layout/AuthLayout.razor Components/App.razor Components/Routes.razor Components/_Imports.razor Components/Pages/NotFound.razor Components/Pages/Error.razor
git commit -m "feat: wire up Identity auth, render-mode gating and auth layout"
```

---

## Task 5: Login, Registration, and Logout pages

**Files:**
- Create: `Components/Account/Pages/Login.razor`
- Create: `Components/Account/Pages/Register.razor`
- Create: `Components/Account/Pages/Logout.razor`

**Interfaces:**
- Consumes: `SignInManager<ApplicationUser>`, `UserManager<ApplicationUser>`, `IdentityRedirectManager` from Task 4.
- Produces: routes `/login`, `/register`, `/Account/Logout` (component). Static-rendered via `[ExcludeFromInteractiveRouting]`.

> These pages use **static SSR** with `<EditForm>` + `[SupplyParameterFromForm]` so the auth cookie is written on a real HTTP POST. MudBlazor components provide the chrome (`MudPaper`, `MudText`, `MudButton type=submit`); the actual inputs use Blazor's `<InputText>` (interactive MudTextField two-way binding does not work under static rendering).

- [ ] **Step 1: Create the Login page**

Create `Components/Account/Pages/Login.razor`:

```razor
@page "/login"
@layout Nook.Components.Layout.AuthLayout
@attribute [AllowAnonymous]
@attribute [ExcludeFromInteractiveRouting]

@using System.ComponentModel.DataAnnotations
@using Microsoft.AspNetCore.Identity
@using Nook.Components.Account
@using Nook.Models

@inject SignInManager<ApplicationUser> SignInManager
@inject IdentityRedirectManager RedirectManager
@inject NavigationManager Nav

<PageTitle>Sign in · Nook</PageTitle>

<MudPaper Class="pa-8" Elevation="3">
    <MudText Typo="Typo.h4" GutterBottom="true">Welcome back</MudText>
    <MudText Typo="Typo.body2" Class="mb-4 mud-text-secondary">Sign in to your Nook.</MudText>

    @if (!string.IsNullOrEmpty(_errorMessage))
    {
        <MudAlert Severity="Severity.Error" Class="mb-4">@_errorMessage</MudAlert>
    }

    <EditForm Model="Input" method="post" OnValidSubmit="LoginUser" FormName="login">
        <DataAnnotationsValidator />
        <div class="mb-3">
            <label class="mud-input-label">Email</label>
            <InputText class="mud-input-root mud-input-root-outlined" @bind-Value="Input.Email"
                       autocomplete="username" style="width:100%;padding:8px;" />
            <ValidationMessage For="() => Input.Email" />
        </div>
        <div class="mb-3">
            <label class="mud-input-label">Password</label>
            <InputText type="password" class="mud-input-root mud-input-root-outlined"
                       @bind-Value="Input.Password" autocomplete="current-password"
                       style="width:100%;padding:8px;" />
            <ValidationMessage For="() => Input.Password" />
        </div>
        <MudButton ButtonType="ButtonType.Submit" Variant="Variant.Filled" Color="Color.Primary"
                   FullWidth="true" Class="mt-2">Sign in</MudButton>
    </EditForm>

    <MudText Typo="Typo.body2" Class="mt-4">
        No account? <MudLink Href="/register">Create one</MudLink>
    </MudText>
</MudPaper>

@code {
    [CascadingParameter] private HttpContext HttpContext { get; set; } = default!;

    [SupplyParameterFromForm] private LoginInput Input { get; set; } = new();
    [SupplyParameterFromQuery] private string? ReturnUrl { get; set; }

    private string? _errorMessage;

    public async Task LoginUser()
    {
        var result = await SignInManager.PasswordSignInAsync(
            Input.Email, Input.Password, isPersistent: true, lockoutOnFailure: false);

        if (result.Succeeded)
        {
            RedirectManager.RedirectTo(ReturnUrl ?? "dashboard");
        }
        else
        {
            _errorMessage = "Invalid email or password.";
        }
    }

    private sealed class LoginInput
    {
        [Required, EmailAddress] public string Email { get; set; } = "";
        [Required, DataType(DataType.Password)] public string Password { get; set; } = "";
    }
}
```

- [ ] **Step 2: Create the Registration page**

Create `Components/Account/Pages/Register.razor`:

```razor
@page "/register"
@layout Nook.Components.Layout.AuthLayout
@attribute [AllowAnonymous]
@attribute [ExcludeFromInteractiveRouting]

@using System.ComponentModel.DataAnnotations
@using Microsoft.AspNetCore.Identity
@using Nook.Components.Account
@using Nook.Models

@inject UserManager<ApplicationUser> UserManager
@inject SignInManager<ApplicationUser> SignInManager
@inject IdentityRedirectManager RedirectManager

<PageTitle>Create account · Nook</PageTitle>

<MudPaper Class="pa-8" Elevation="3">
    <MudText Typo="Typo.h4" GutterBottom="true">Create your Nook</MudText>

    @if (_errors.Count > 0)
    {
        <MudAlert Severity="Severity.Error" Class="mb-4">
            @foreach (var e in _errors) { <div>@e</div> }
        </MudAlert>
    }

    <EditForm Model="Input" method="post" OnValidSubmit="RegisterUser" FormName="register">
        <DataAnnotationsValidator />
        <div class="mb-3">
            <label class="mud-input-label">Email</label>
            <InputText class="mud-input-root mud-input-root-outlined" @bind-Value="Input.Email"
                       autocomplete="username" style="width:100%;padding:8px;" />
            <ValidationMessage For="() => Input.Email" />
        </div>
        <div class="mb-3">
            <label class="mud-input-label">Password</label>
            <InputText type="password" class="mud-input-root mud-input-root-outlined"
                       @bind-Value="Input.Password" autocomplete="new-password"
                       style="width:100%;padding:8px;" />
            <ValidationMessage For="() => Input.Password" />
        </div>
        <div class="mb-3">
            <label class="mud-input-label">Confirm password</label>
            <InputText type="password" class="mud-input-root mud-input-root-outlined"
                       @bind-Value="Input.ConfirmPassword" autocomplete="new-password"
                       style="width:100%;padding:8px;" />
            <ValidationMessage For="() => Input.ConfirmPassword" />
        </div>
        <MudButton ButtonType="ButtonType.Submit" Variant="Variant.Filled" Color="Color.Primary"
                   FullWidth="true" Class="mt-2">Create account</MudButton>
    </EditForm>

    <MudText Typo="Typo.body2" Class="mt-4">
        Already have an account? <MudLink Href="/login">Sign in</MudLink>
    </MudText>
</MudPaper>

@code {
    [SupplyParameterFromForm] private RegisterInput Input { get; set; } = new();
    private readonly List<string> _errors = new();

    public async Task RegisterUser()
    {
        _errors.Clear();
        var user = new ApplicationUser { UserName = Input.Email, Email = Input.Email };
        var result = await UserManager.CreateAsync(user, Input.Password);
        if (!result.Succeeded)
        {
            _errors.AddRange(result.Errors.Select(e => e.Description));
            return;
        }
        await SignInManager.SignInAsync(user, isPersistent: true);
        RedirectManager.RedirectTo("dashboard");
    }

    private sealed class RegisterInput
    {
        [Required, EmailAddress] public string Email { get; set; } = "";
        [Required, StringLength(100, MinimumLength = 6)]
        [DataType(DataType.Password)] public string Password { get; set; } = "";
        [DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; } = "";
    }
}
```

- [ ] **Step 3: Create the Logout page**

Create `Components/Account/Pages/Logout.razor`:

```razor
@page "/Account/Logout"
@layout Nook.Components.Layout.AuthLayout
@attribute [ExcludeFromInteractiveRouting]

<form action="Account/Logout" method="post">
    <AntiforgeryToken />
    <input type="hidden" name="returnUrl" value="" />
    <MudPaper Class="pa-8 d-flex flex-column align-center" Elevation="3">
        <MudText Typo="Typo.h6" Class="mb-4">Sign out of Nook?</MudText>
        <MudButton ButtonType="ButtonType.Submit" Variant="Variant.Filled" Color="Color.Primary">
            Sign out
        </MudButton>
    </MudPaper>
</form>
```

> The POST hits the `MapAdditionalIdentityEndpoints` `/Account/Logout` endpoint from Task 4, which signs out and redirects to `~/` (the homepage).

- [ ] **Step 4: Build**

Run: `dotnet build`
Expected: Build succeeds.

- [ ] **Step 5: Manual verification of the full auth loop**

Run: `dotnet watch`. Then:
1. Visit `/register`, create `me@test.com` / `password`. Expected: redirected to `/dashboard`, signed in.
2. Visit `/Account/Logout`, click Sign out. Expected: signed out, sent to `/`.
3. Visit `/login`, sign in as the demo user `demo@nook.local` / `Demo123!`. Expected: dashboard loads with the seeded items.

Stop the app.

- [ ] **Step 6: Commit**

```bash
git add Components/Account/Pages
git commit -m "feat: login, registration and logout pages"
```

---

## Task 6: Test project, `ICurrentUser`, and the activity service

**Files:**
- Create: `Nook.Tests/Nook.Tests.csproj`
- Create: `Nook.Tests/TestDbContextFactory.cs`
- Create: `Nook.Tests/FakeCurrentUser.cs`
- Create: `Services/ICurrentUser.cs`
- Create: `Services/CurrentUser.cs`
- Create: `Services/IActivityService.cs`
- Create: `Services/ActivityService.cs`
- Create: `Nook.Tests/ActivityServiceTests.cs`
- Modify: `Nook.sln`, `Program.cs`

**Interfaces:**
- Produces:
  - `ICurrentUser` → `Task<string?> GetUserIdAsync()` and `Task<string> GetRequiredUserIdAsync()`.
  - `IActivityService` → `Task LogAsync(string userId, ActivityType type, int? itemId, string itemTitle, string? detail = null)`, `Task<List<ActivityLog>> GetForUserAsync(string userId, ActivityType? type = null, DateTime? from = null, DateTime? to = null, int? take = null)`.
  - Test helpers `TestDbContextFactory` (`IDbContextFactory<NookContext>` over EF InMemory) and `FakeCurrentUser`.

- [ ] **Step 1: Create the test project and register it in the solution**

Create `Nook.Tests/Nook.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="10.0.9" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Nook.csproj" />
  </ItemGroup>

</Project>
```

Run: `dotnet sln add Nook.Tests/Nook.Tests.csproj`
Expected: "Project ... added to the solution."

- [ ] **Step 2: Add test helpers**

Create `Nook.Tests/TestDbContextFactory.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Nook.Data;

namespace Nook.Tests;

/// <summary>
/// An IDbContextFactory backed by a uniquely-named EF Core InMemory database,
/// so each test gets isolated state. Matches the production factory pattern.
/// </summary>
public sealed class TestDbContextFactory : IDbContextFactory<NookContext>
{
    private readonly DbContextOptions<NookContext> _options;

    public TestDbContextFactory()
    {
        _options = new DbContextOptionsBuilder<NookContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
    }

    public NookContext CreateDbContext() => new(_options);

    public Task<NookContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(CreateDbContext());
}
```

Create `Nook.Tests/FakeCurrentUser.cs`:

```csharp
using Nook.Services;

namespace Nook.Tests;

public sealed class FakeCurrentUser : ICurrentUser
{
    private readonly string? _userId;
    public FakeCurrentUser(string? userId) => _userId = userId;

    public Task<string?> GetUserIdAsync() => Task.FromResult(_userId);

    public Task<string> GetRequiredUserIdAsync() =>
        _userId is null
            ? throw new InvalidOperationException("No current user.")
            : Task.FromResult(_userId);
}
```

- [ ] **Step 3: Define `ICurrentUser` and the production implementation**

Create `Services/ICurrentUser.cs`:

```csharp
namespace Nook.Services;

/// <summary>Resolves the signed-in user's id for the service layer.</summary>
public interface ICurrentUser
{
    /// <summary>The current user id, or null if not authenticated.</summary>
    Task<string?> GetUserIdAsync();

    /// <summary>The current user id; throws if not authenticated.</summary>
    Task<string> GetRequiredUserIdAsync();
}
```

Create `Services/CurrentUser.cs`:

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace Nook.Services;

/// <summary>
/// Resolves the current user id from the cascaded Blazor AuthenticationState.
/// </summary>
public sealed class CurrentUser : ICurrentUser
{
    private readonly AuthenticationStateProvider _authProvider;

    public CurrentUser(AuthenticationStateProvider authProvider)
    {
        _authProvider = authProvider;
    }

    public async Task<string?> GetUserIdAsync()
    {
        var state = await _authProvider.GetAuthenticationStateAsync();
        return state.User.FindFirstValue(ClaimTypes.NameIdentifier);
    }

    public async Task<string> GetRequiredUserIdAsync()
        => await GetUserIdAsync()
           ?? throw new InvalidOperationException("No authenticated user in the current context.");
}
```

- [ ] **Step 4: Define `IActivityService`**

Create `Services/IActivityService.cs`:

```csharp
using Nook.Models;

namespace Nook.Services;

/// <summary>Writes and queries the activity audit log.</summary>
public interface IActivityService
{
    Task LogAsync(string userId, ActivityType type, int? itemId, string itemTitle, string? detail = null);

    Task<List<ActivityLog>> GetForUserAsync(
        string userId, ActivityType? type = null, DateTime? from = null, DateTime? to = null, int? take = null);
}
```

- [ ] **Step 5: Write the failing test for `ActivityService`**

Create `Nook.Tests/ActivityServiceTests.cs`:

```csharp
using Nook.Models;
using Nook.Services;
using Xunit;

namespace Nook.Tests;

public class ActivityServiceTests
{
    [Fact]
    public async Task LogAsync_then_GetForUser_returns_only_that_users_rows_newest_first()
    {
        var factory = new TestDbContextFactory();
        var sut = new ActivityService(factory);

        await sut.LogAsync("user-a", ActivityType.Created, 1, "First", null);
        await sut.LogAsync("user-a", ActivityType.Completed, 1, "First", "done");
        await sut.LogAsync("user-b", ActivityType.Created, 2, "Other", null);

        var rows = await sut.GetForUserAsync("user-a");

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal("user-a", r.UserId));
        // Newest first.
        Assert.True(rows[0].Timestamp >= rows[1].Timestamp);
    }

    [Fact]
    public async Task GetForUserAsync_filters_by_type()
    {
        var factory = new TestDbContextFactory();
        var sut = new ActivityService(factory);

        await sut.LogAsync("u", ActivityType.Created, 1, "X", null);
        await sut.LogAsync("u", ActivityType.Completed, 1, "X", null);

        var completed = await sut.GetForUserAsync("u", type: ActivityType.Completed);

        Assert.Single(completed);
        Assert.Equal(ActivityType.Completed, completed[0].Type);
    }
}
```

- [ ] **Step 6: Run the test to confirm it fails**

Run: `dotnet test Nook.Tests/Nook.Tests.csproj`
Expected: FAIL — `ActivityService` does not exist (compile error).

- [ ] **Step 7: Implement `ActivityService`**

Create `Services/ActivityService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Nook.Data;
using Nook.Models;

namespace Nook.Services;

public sealed class ActivityService : IActivityService
{
    private readonly IDbContextFactory<NookContext> _factory;

    public ActivityService(IDbContextFactory<NookContext> factory)
    {
        _factory = factory;
    }

    public async Task LogAsync(string userId, ActivityType type, int? itemId, string itemTitle, string? detail = null)
    {
        await using var db = await _factory.CreateDbContextAsync();
        db.ActivityLogs.Add(new ActivityLog
        {
            UserId = userId,
            Type = type,
            ItemId = itemId,
            ItemTitle = itemTitle.Length > 300 ? itemTitle[..300] : itemTitle,
            Detail = detail,
            Timestamp = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    public async Task<List<ActivityLog>> GetForUserAsync(
        string userId, ActivityType? type = null, DateTime? from = null, DateTime? to = null, int? take = null)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var query = db.ActivityLogs.Where(a => a.UserId == userId);

        if (type.HasValue) query = query.Where(a => a.Type == type.Value);
        if (from.HasValue) query = query.Where(a => a.Timestamp >= from.Value);
        if (to.HasValue) query = query.Where(a => a.Timestamp <= to.Value);

        query = query.OrderByDescending(a => a.Timestamp).ThenByDescending(a => a.ActivityLogId);
        if (take.HasValue) query = query.Take(take.Value);

        return await query.ToListAsync();
    }
}
```

- [ ] **Step 8: Run the test to confirm it passes**

Run: `dotnet test Nook.Tests/Nook.Tests.csproj`
Expected: PASS (2 tests).

- [ ] **Step 9: Register the new services**

In `Program.cs`, alongside the existing `AddScoped<IItemService, ItemService>()` lines, add:

```csharp
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<IActivityService, ActivityService>();
```

Add `using Nook.Services;` if not already present (it is). Run `dotnet build`. Expected: succeeds.

- [ ] **Step 10: Commit**

```bash
git add Nook.Tests Nook.sln Services/ICurrentUser.cs Services/CurrentUser.cs Services/IActivityService.cs Services/ActivityService.cs Program.cs
git commit -m "feat: test project, ICurrentUser and ActivityService"
```

---

## Task 7: Scope `ItemService`/`TagService` by user and log activity

**Files:**
- Modify: `Services/ItemService.cs`
- Modify: `Services/TagService.cs`
- Create: `Nook.Tests/ItemServiceScopingTests.cs`

**Interfaces:**
- Consumes: `ICurrentUser`, `IActivityService` from Task 6; `ItemFilter` (unchanged signature).
- Produces: `ItemService(IDbContextFactory<NookContext>, ICurrentUser, IActivityService)`; `TagService(IDbContextFactory<NookContext>, ICurrentUser)`. All queries filtered by the current user. `CreateAsync` stamps `UserId`. Each mutation writes one `ActivityLog`. `TagService.CreateAsync`/`GetOrCreateAsync` stamp `UserId` and dedupe per-user.

- [ ] **Step 1: Write failing scoping + logging tests**

Create `Nook.Tests/ItemServiceScopingTests.cs`:

```csharp
using Nook.Models;
using Nook.Services;
using Xunit;

namespace Nook.Tests;

public class ItemServiceScopingTests
{
    private static (ItemService svc, IActivityService activity, TestDbContextFactory factory)
        MakeService(string userId)
    {
        var factory = new TestDbContextFactory();
        var activity = new ActivityService(factory);
        var svc = new ItemService(factory, new FakeCurrentUser(userId), activity);
        return (svc, activity, factory);
    }

    [Fact]
    public async Task GetItemsAsync_returns_only_current_users_items()
    {
        var (svcA, _, factory) = MakeService("user-a");
        await svcA.CreateAsync(new Item { Title = "A's note", ItemType = ItemType.Note });

        var svcB = new ItemService(factory, new FakeCurrentUser("user-b"), new ActivityService(factory));
        await svcB.CreateAsync(new Item { Title = "B's note", ItemType = ItemType.Note });

        var aItems = await svcA.GetItemsAsync(new ItemFilter());
        var bItems = await svcB.GetItemsAsync(new ItemFilter());

        Assert.Single(aItems);
        Assert.Equal("A's note", aItems[0].Title);
        Assert.Single(bItems);
        Assert.Equal("B's note", bItems[0].Title);
    }

    [Fact]
    public async Task CreateAsync_stamps_userId_and_logs_Created()
    {
        var (svc, activity, _) = MakeService("user-a");

        var item = await svc.CreateAsync(new Item { Title = "New", ItemType = ItemType.Note });

        Assert.Equal("user-a", item.UserId);
        var log = await activity.GetForUserAsync("user-a");
        Assert.Single(log);
        Assert.Equal(ActivityType.Created, log[0].Type);
        Assert.Equal(item.ItemId, log[0].ItemId);
    }

    [Fact]
    public async Task CompleteAsync_logs_Completed_and_ignores_other_users_items()
    {
        var (svcA, activityA, factory) = MakeService("user-a");
        var item = await svcA.CreateAsync(new Item { Title = "Todo", ItemType = ItemType.Todo });

        // user-b cannot complete user-a's item.
        var svcB = new ItemService(factory, new FakeCurrentUser("user-b"), new ActivityService(factory));
        await svcB.CompleteAsync(item.ItemId);
        var afterB = await svcA.GetByIdAsync(item.ItemId);
        Assert.NotEqual(ItemStatus.Done, afterB!.Status);

        // user-a can.
        await svcA.CompleteAsync(item.ItemId);
        var afterA = await svcA.GetByIdAsync(item.ItemId);
        Assert.Equal(ItemStatus.Done, afterA!.Status);

        var completedLogs = await activityA.GetForUserAsync("user-a", ActivityType.Completed);
        Assert.Single(completedLogs);
    }
}
```

- [ ] **Step 2: Run to confirm failure**

Run: `dotnet test Nook.Tests/Nook.Tests.csproj`
Expected: FAIL — `ItemService` constructor signature does not match (no `ICurrentUser`/`IActivityService`).

- [ ] **Step 3: Rewrite `ItemService` with scoping and logging**

Replace `Services/ItemService.cs` with the version below. Every read is filtered by `userId`; every mutation verifies ownership and writes one `ActivityLog`.

```csharp
using Microsoft.EntityFrameworkCore;
using Nook.Data;
using Nook.Models;

namespace Nook.Services;

public class ItemService : IItemService
{
    private readonly IDbContextFactory<NookContext> _factory;
    private readonly ICurrentUser _currentUser;
    private readonly IActivityService _activity;

    public ItemService(IDbContextFactory<NookContext> factory, ICurrentUser currentUser, IActivityService activity)
    {
        _factory = factory;
        _currentUser = currentUser;
        _activity = activity;
    }

    private static IQueryable<Item> WithTags(IQueryable<Item> query) =>
        query.Include(i => i.ItemTags).ThenInclude(it => it.Tag);

    public async Task<List<Item>> GetItemsAsync(ItemFilter filter)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        var query = WithTags(db.Items).Where(i => i.UserId == userId);

        query = filter.ShowArchived
            ? query.Where(i => i.ArchivedAt != null)
            : query.Where(i => i.ArchivedAt == null);

        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            var s = filter.SearchText.Trim();
            query = query.Where(i =>
                i.Title.Contains(s) ||
                (i.Body != null && i.Body.Contains(s)) ||
                (i.Url != null && i.Url.Contains(s)) ||
                i.ItemTags.Any(it => it.Tag.Name.Contains(s)));
        }

        if (filter.ItemType.HasValue) query = query.Where(i => i.ItemType == filter.ItemType.Value);
        if (filter.Status.HasValue) query = query.Where(i => i.Status == filter.Status.Value);
        if (filter.Priority.HasValue) query = query.Where(i => i.Priority == filter.Priority.Value);
        if (filter.TagId.HasValue) query = query.Where(i => i.ItemTags.Any(it => it.TagId == filter.TagId.Value));
        if (filter.FavoritesOnly) query = query.Where(i => i.IsFavorite);
        if (filter.PinnedOnly) query = query.Where(i => i.IsPinned);

        var now = DateTime.UtcNow;
        if (filter.Overdue)
            query = query.Where(i => i.DueDate != null && i.DueDate < now && i.Status != ItemStatus.Done);
        if (filter.DueSoon)
        {
            var horizon = now.AddDays(filter.DueSoonDays);
            query = query.Where(i => i.DueDate != null && i.DueDate >= now
                                     && i.DueDate <= horizon && i.Status != ItemStatus.Done);
        }

        return await query
            .OrderByDescending(i => i.IsPinned)
            .ThenByDescending(i => i.UpdatedAt)
            .ToListAsync();
    }

    public async Task<Item?> GetByIdAsync(int id)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Items
            .Include(i => i.ItemTags).ThenInclude(it => it.Tag)
            .Include(i => i.Parent)
            .Include(i => i.Children)
            .Include(i => i.OutgoingLinks).ThenInclude(l => l.TargetItem)
            .Include(i => i.IncomingLinks).ThenInclude(l => l.SourceItem)
            .FirstOrDefaultAsync(i => i.ItemId == id && i.UserId == userId);
    }

    public async Task<Item> CreateAsync(Item item, IEnumerable<int>? tagIds = null)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        item.UserId = userId;
        db.Items.Add(item);
        if (tagIds != null)
        {
            foreach (var tagId in tagIds.Distinct())
                item.ItemTags.Add(new ItemTag { TagId = tagId });
        }
        await db.SaveChangesAsync();
        await _activity.LogAsync(userId, ActivityType.Created, item.ItemId, item.Title);
        return item;
    }

    public async Task UpdateAsync(Item item, IEnumerable<int>? tagIds = null)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        var existing = await db.Items
            .Include(i => i.ItemTags)
            .FirstOrDefaultAsync(i => i.ItemId == item.ItemId && i.UserId == userId);
        if (existing is null) return;

        existing.Title = item.Title;
        existing.Body = item.Body;
        existing.ItemType = item.ItemType;
        existing.Status = item.Status;
        existing.Priority = item.Priority;
        existing.DueDate = item.DueDate;
        existing.ReminderDate = item.ReminderDate;
        existing.CompletedDate = item.CompletedDate;
        existing.Url = item.Url;
        existing.ParentItemId = item.ParentItemId;
        existing.IsPinned = item.IsPinned;
        existing.IsFavorite = item.IsFavorite;
        existing.ArchivedAt = item.ArchivedAt;

        if (tagIds is not null)
        {
            var desired = tagIds.Distinct().ToHashSet();
            foreach (var remove in existing.ItemTags.Where(it => !desired.Contains(it.TagId)).ToList())
                existing.ItemTags.Remove(remove);
            var current = existing.ItemTags.Select(it => it.TagId).ToHashSet();
            foreach (var tagId in desired.Where(t => !current.Contains(t)))
                existing.ItemTags.Add(new ItemTag { ItemId = existing.ItemId, TagId = tagId });
        }

        await db.SaveChangesAsync();
        await _activity.LogAsync(userId, ActivityType.Updated, existing.ItemId, existing.Title);
    }

    public async Task DeleteAsync(int id)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        var item = await db.Items.Include(i => i.Children)
            .FirstOrDefaultAsync(i => i.ItemId == id && i.UserId == userId);
        if (item is null) return;

        foreach (var child in item.Children) child.ParentItemId = null;
        var links = await db.ItemLinks
            .Where(l => l.SourceItemId == id || l.TargetItemId == id).ToListAsync();
        db.ItemLinks.RemoveRange(links);

        var title = item.Title;
        db.Items.Remove(item);
        await db.SaveChangesAsync();
        await _activity.LogAsync(userId, ActivityType.Deleted, null, title);
    }

    public Task ArchiveAsync(int id) =>
        MutateAsync(id, i => i.ArchivedAt = DateTime.UtcNow, ActivityType.Archived);
    public Task UnarchiveAsync(int id) =>
        MutateAsync(id, i => i.ArchivedAt = null, ActivityType.Unarchived);
    public Task TogglePinAsync(int id) =>
        MutateAsync(id, i => i.IsPinned = !i.IsPinned, ActivityType.Updated);
    public Task ToggleFavoriteAsync(int id) =>
        MutateAsync(id, i => i.IsFavorite = !i.IsFavorite, ActivityType.Updated);

    public Task CompleteAsync(int id) => MutateAsync(id, i =>
    {
        i.Status = ItemStatus.Done;
        i.CompletedDate = DateTime.UtcNow;
    }, ActivityType.Completed);

    public Task ReopenAsync(int id) => MutateAsync(id, i =>
    {
        i.Status = ItemStatus.Open;
        i.CompletedDate = null;
    }, ActivityType.Reopened);

    private async Task MutateAsync(int id, Action<Item> mutate, ActivityType activityType)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        var item = await db.Items.FirstOrDefaultAsync(i => i.ItemId == id && i.UserId == userId);
        if (item is null) return;
        mutate(item);
        await db.SaveChangesAsync();
        await _activity.LogAsync(userId, activityType, item.ItemId, item.Title);
    }

    public async Task<List<Item>> GetRelatedByTagsAsync(int id, int max = 10)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        var tagIds = await db.ItemTags
            .Where(it => it.ItemId == id)
            .Select(it => it.TagId).ToListAsync();
        if (tagIds.Count == 0) return new List<Item>();

        return await WithTags(db.Items)
            .Where(i => i.UserId == userId && i.ItemId != id && i.ArchivedAt == null
                        && i.ItemTags.Any(it => tagIds.Contains(it.TagId)))
            .OrderByDescending(i => i.ItemTags.Count(it => tagIds.Contains(it.TagId)))
            .ThenByDescending(i => i.UpdatedAt)
            .Take(max).ToListAsync();
    }

    public async Task<List<Item>> GetChildrenAsync(int id)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        return await WithTags(db.Items)
            .Where(i => i.UserId == userId && i.ParentItemId == id)
            .OrderBy(i => i.CreatedAt).ToListAsync();
    }

    public async Task LinkAsync(int sourceId, int targetId, string? linkType = null)
    {
        if (sourceId == targetId) return;
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        // Only link items the user owns.
        var owns = await db.Items.CountAsync(i =>
            i.UserId == userId && (i.ItemId == sourceId || i.ItemId == targetId));
        if (owns < 2) return;
        var exists = await db.ItemLinks
            .AnyAsync(l => l.SourceItemId == sourceId && l.TargetItemId == targetId);
        if (exists) return;
        db.ItemLinks.Add(new ItemLink
        {
            SourceItemId = sourceId,
            TargetItemId = targetId,
            LinkType = linkType,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    public async Task UnlinkAsync(int itemLinkId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var link = await db.ItemLinks.FindAsync(itemLinkId);
        if (link is null) return;
        db.ItemLinks.Remove(link);
        await db.SaveChangesAsync();
    }

    // ---- Dashboard / Reminders / Todos: all scoped by user ----

    private async Task<List<Item>> ScopedActiveAsync(Func<IQueryable<Item>, IQueryable<Item>> shape)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        var baseQuery = WithTags(db.Items).Where(i => i.UserId == userId && i.ArchivedAt == null);
        return await shape(baseQuery).ToListAsync();
    }

    public Task<List<Item>> GetRecentlyCreatedAsync(int count = 5) =>
        ScopedActiveAsync(q => q.OrderByDescending(i => i.CreatedAt).Take(count));

    public Task<List<Item>> GetRecentlyUpdatedAsync(int count = 5) =>
        ScopedActiveAsync(q => q.OrderByDescending(i => i.UpdatedAt).Take(count));

    public Task<List<Item>> GetDueSoonAsync(int days = 7, int count = 10)
    {
        var now = DateTime.UtcNow;
        var horizon = now.AddDays(days);
        return ScopedActiveAsync(q => q
            .Where(i => i.Status != ItemStatus.Done && i.DueDate != null
                        && i.DueDate >= now && i.DueDate <= horizon)
            .OrderBy(i => i.DueDate).Take(count));
    }

    public Task<List<Item>> GetOverdueAsync(int count = 10)
    {
        var now = DateTime.UtcNow;
        return ScopedActiveAsync(q => q
            .Where(i => i.Status != ItemStatus.Done && i.DueDate != null && i.DueDate < now)
            .OrderBy(i => i.DueDate).Take(count));
    }

    public Task<List<Item>> GetPinnedAsync(int count = 10) =>
        ScopedActiveAsync(q => q.Where(i => i.IsPinned).OrderByDescending(i => i.UpdatedAt).Take(count));

    public Task<List<Item>> GetFavoritesAsync(int count = 10) =>
        ScopedActiveAsync(q => q.Where(i => i.IsFavorite).OrderByDescending(i => i.UpdatedAt).Take(count));

    public Task<List<Item>> GetUpcomingRemindersAsync()
    {
        var now = DateTime.UtcNow;
        return ScopedActiveAsync(q => q
            .Where(i => i.Status != ItemStatus.Done && i.ReminderDate != null && i.ReminderDate >= now)
            .OrderBy(i => i.ReminderDate));
    }

    public Task<List<Item>> GetOverdueRemindersAsync()
    {
        var now = DateTime.UtcNow;
        return ScopedActiveAsync(q => q
            .Where(i => i.Status != ItemStatus.Done && i.ReminderDate != null && i.ReminderDate < now)
            .OrderBy(i => i.ReminderDate));
    }

    public Task<List<Item>> GetTodosAsync(bool includeCompleted = false) =>
        ScopedActiveAsync(q =>
        {
            q = q.Where(i => i.ItemType == ItemType.Todo);
            if (!includeCompleted) q = q.Where(i => i.Status != ItemStatus.Done);
            return q.OrderByDescending(i => i.IsPinned)
                    .ThenBy(i => i.DueDate == null)
                    .ThenBy(i => i.DueDate)
                    .ThenByDescending(i => i.CreatedAt);
        });
}
```

- [ ] **Step 4: Scope `TagService` by user**

Replace `Services/TagService.cs` with a user-scoped version (constructor adds `ICurrentUser`; all queries filter by `UserId`; create/get-or-create stamp `UserId` and dedupe per user):

```csharp
using Microsoft.EntityFrameworkCore;
using Nook.Data;
using Nook.Models;

namespace Nook.Services;

public class TagService : ITagService
{
    private readonly IDbContextFactory<NookContext> _factory;
    private readonly ICurrentUser _currentUser;

    public TagService(IDbContextFactory<NookContext> factory, ICurrentUser currentUser)
    {
        _factory = factory;
        _currentUser = currentUser;
    }

    public async Task<List<Tag>> GetAllAsync()
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Tags.Where(t => t.UserId == userId).OrderBy(t => t.Name).ToListAsync();
    }

    public async Task<Tag?> GetByIdAsync(int id)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Tags.FirstOrDefaultAsync(t => t.TagId == id && t.UserId == userId);
    }

    public async Task<Tag> CreateAsync(string name, string? color = null)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        name = name.Trim();
        await using var db = await _factory.CreateDbContextAsync();
        if (await db.Tags.AnyAsync(t => t.UserId == userId && t.Name == name))
            throw new InvalidOperationException($"A tag named \"{name}\" already exists.");
        var tag = new Tag { Name = name, Color = color, UserId = userId };
        db.Tags.Add(tag);
        await db.SaveChangesAsync();
        return tag;
    }

    public async Task<Tag> GetOrCreateAsync(string name, string? color = null)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        name = name.Trim();
        await using var db = await _factory.CreateDbContextAsync();
        var existing = await db.Tags.FirstOrDefaultAsync(t => t.UserId == userId && t.Name == name);
        if (existing is not null) return existing;
        var tag = new Tag { Name = name, Color = color, UserId = userId };
        db.Tags.Add(tag);
        await db.SaveChangesAsync();
        return tag;
    }

    public async Task UpdateAsync(Tag tag)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        var existing = await db.Tags.FirstOrDefaultAsync(t => t.TagId == tag.TagId && t.UserId == userId);
        if (existing is null) return;
        existing.Name = tag.Name.Trim();
        existing.Color = tag.Color;
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        var tag = await db.Tags.FirstOrDefaultAsync(t => t.TagId == id && t.UserId == userId);
        if (tag is null) return;
        db.Tags.Remove(tag);
        await db.SaveChangesAsync();
    }

    public async Task<List<TagSummary>> GetTagSummaryAsync()
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        // Order on the source before projecting (the projected record can't be ordered in SQL).
        return await db.Tags
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.ItemTags.Count)
            .ThenBy(t => t.Name)
            .Select(t => new TagSummary(t.TagId, t.Name, t.Color, t.ItemTags.Count))
            .ToListAsync();
    }

    public async Task AssignTagAsync(int itemId, int tagId)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        // Both item and tag must belong to the user.
        var ownsItem = await db.Items.AnyAsync(i => i.ItemId == itemId && i.UserId == userId);
        var ownsTag = await db.Tags.AnyAsync(t => t.TagId == tagId && t.UserId == userId);
        if (!ownsItem || !ownsTag) return;
        if (await db.ItemTags.AnyAsync(it => it.ItemId == itemId && it.TagId == tagId)) return;
        db.ItemTags.Add(new ItemTag { ItemId = itemId, TagId = tagId });
        await db.SaveChangesAsync();
    }

    public async Task RemoveTagAsync(int itemId, int tagId)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        var ownsItem = await db.Items.AnyAsync(i => i.ItemId == itemId && i.UserId == userId);
        if (!ownsItem) return;
        var link = await db.ItemTags.FirstOrDefaultAsync(it => it.ItemId == itemId && it.TagId == tagId);
        if (link is null) return;
        db.ItemTags.Remove(link);
        await db.SaveChangesAsync();
    }
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test Nook.Tests/Nook.Tests.csproj`
Expected: PASS (all tests, including the 3 new scoping tests).

- [ ] **Step 6: Build the app to confirm DI still resolves**

Run: `dotnet build`
Expected: succeeds (the new constructor params are already registered from Task 6).

- [ ] **Step 7: Commit**

```bash
git add Services/ItemService.cs Services/TagService.cs Nook.Tests/ItemServiceScopingTests.cs
git commit -m "feat: scope items/tags by user and log item activity"
```

---

## Task 8: Homepage / landing page

**Files:**
- Modify: `Components/Pages/Dashboard.razor` (confirm route; no change to its content) — *read only, no edit unless route conflicts*
- Create: `Components/Pages/Home.razor`

**Interfaces:**
- Produces: anonymous route `/` (landing) that redirects authenticated users to `/dashboard`.

- [ ] **Step 1: Create the Home page**

Create `Components/Pages/Home.razor`:

```razor
@page "/"
@layout Nook.Components.Layout.AuthLayout
@attribute [AllowAnonymous]

@inject AuthenticationStateProvider AuthProvider
@inject NavigationManager Nav

<PageTitle>Nook — your personal nook for everything</PageTitle>

<div class="d-flex flex-column align-center text-center">
    <MudText Typo="Typo.h2" GutterBottom="true">Nook</MudText>
    <MudText Typo="Typo.h6" Class="mb-6 mud-text-secondary" Style="max-width:540px;">
        One calm place for your notes, todos, reminders, bookmarks, ideas and more —
        everything you capture, organized by tags and surfaced when you need it.
    </MudText>

    <div class="d-flex gap-4 mb-8">
        <MudButton Variant="Variant.Filled" Color="Color.Primary" Size="Size.Large" Href="/register">
            Get started
        </MudButton>
        <MudButton Variant="Variant.Outlined" Color="Color.Primary" Size="Size.Large" Href="/login">
            Sign in
        </MudButton>
    </div>

    <MudGrid Justify="Justify.Center" Class="mt-4">
        <MudItem xs="12" sm="4">
            <MudIcon Icon="@Icons.Material.Filled.Bolt" Size="Size.Large" Color="Color.Primary" />
            <MudText Typo="Typo.subtitle1">Capture fast</MudText>
            <MudText Typo="Typo.body2" Class="mud-text-secondary">Everything is an item. One box, any kind.</MudText>
        </MudItem>
        <MudItem xs="12" sm="4">
            <MudIcon Icon="@Icons.Material.Filled.Timeline" Size="Size.Large" Color="Color.Primary" />
            <MudText Typo="Typo.subtitle1">See your story</MudText>
            <MudText Typo="Typo.body2" Class="mud-text-secondary">A timeline and analytics that celebrate your progress.</MudText>
        </MudItem>
        <MudItem xs="12" sm="4">
            <MudIcon Icon="@Icons.Material.Filled.Label" Size="Size.Large" Color="Color.Primary" />
            <MudText Typo="Typo.subtitle1">Organize with tags</MudText>
            <MudText Typo="Typo.body2" Class="mud-text-secondary">Jump straight to everything under a tag.</MudText>
        </MudItem>
    </MudGrid>
</div>

@code {
    protected override async Task OnInitializedAsync()
    {
        var state = await AuthProvider.GetAuthenticationStateAsync();
        if (state.User.Identity?.IsAuthenticated == true)
        {
            Nav.NavigateTo("/dashboard");
        }
    }
}
```

> Confirm no other page already uses `@page "/"`. The Dashboard uses `/dashboard` (per the nav menu), so there is no conflict. If a stray `@page "/"` exists on Dashboard, remove it.

- [ ] **Step 2: Build**

Run: `dotnet build`
Expected: succeeds.

- [ ] **Step 3: Manual verification**

Run: `dotnet watch`.
1. Logged out, visit `/`. Expected: the landing page with Get started / Sign in.
2. Sign in, then visit `/`. Expected: redirected to `/dashboard`.
Stop the app.

- [ ] **Step 4: Commit**

```bash
git add Components/Pages/Home.razor
git commit -m "feat: public homepage / landing page"
```

---

## Task 9: Nav menu — tags group, new links, and account section; tag-name filtering on Items

**Files:**
- Modify: `Components/Layout/NavMenu.razor`
- Modify: `Components/Pages/Items.razor` (accept a `tag` name query param)

**Interfaces:**
- Consumes: `ITagService.GetTagSummaryAsync()`, `AuthenticationStateProvider`.
- Produces: nav links for Timeline/Analytics/Log; a collapsible Tags group linking to `/items?tag={name}`; an account section with the user's email and a Logout link. `Items.razor` resolves a `tag` name query param to an `ItemFilter.TagId`.

- [ ] **Step 1: Add a `tag`-name query parameter to `Items.razor`**

`Components/Pages/Items.razor` already injects `ITagService`, has a `private readonly ItemFilter _filter = new();`, loads `_allTags` in `OnInitializedAsync`, and seeds the filter from query params in `OnParametersSetAsync`. Add a new query-parameter property to the `@code` block alongside the existing ones (after the `Type` line, ~line 40):

```csharp
    [SupplyParameterFromQuery(Name = "tag")] public string? TagName { get; set; }
```

- [ ] **Step 2: Resolve the tag name to a `TagId` in `OnParametersSetAsync`**

In `OnParametersSetAsync`, after the existing `_filter.TagId = TagId;` line and before `await ReloadAsync();`, add resolution from the already-loaded `_allTags`:

```csharp
        // A `tag` name from the nav menu wins over a raw `tagId` if both are present.
        if (!string.IsNullOrWhiteSpace(TagName))
        {
            var match = _allTags.FirstOrDefault(t =>
                string.Equals(t.Name, TagName, StringComparison.OrdinalIgnoreCase));
            _filter.TagId = match?.TagId;
        }
```

> `_allTags` is populated by `OnInitializedAsync`, which runs before `OnParametersSetAsync` on first load, so the lookup is safe.

- [ ] **Step 3: Rewrite `NavMenu.razor`**

Replace `Components/Layout/NavMenu.razor`:

```razor
@implements IDisposable
@inject ITagService TagService
@inject AuthenticationStateProvider AuthProvider
@inject NavigationManager Nav

<MudNavMenu>
    <MudNavLink Href="/dashboard" Icon="@Icons.Material.Filled.Dashboard">Dashboard</MudNavLink>
    <MudNavLink Href="/items" Match="NavLinkMatch.All" Icon="@Icons.Material.Filled.ViewList">All Items</MudNavLink>
    <MudNavLink Href="/todos" Icon="@Icons.Material.Filled.CheckBox">Todos</MudNavLink>
    <MudNavLink Href="/reminders" Icon="@Icons.Material.Filled.Alarm">Reminders</MudNavLink>
    <MudNavLink Href="/bookmarks" Icon="@Icons.Material.Filled.Bookmark">Bookmarks</MudNavLink>

    <MudDivider Class="my-2" />
    <MudNavLink Href="/timeline" Icon="@Icons.Material.Filled.Timeline">Timeline</MudNavLink>
    <MudNavLink Href="/analytics" Icon="@Icons.Material.Filled.BarChart">Analytics</MudNavLink>
    <MudNavLink Href="/log" Icon="@Icons.Material.Filled.History">Activity Log</MudNavLink>
    <MudNavLink Href="/archive" Icon="@Icons.Material.Filled.Archive">Archive</MudNavLink>

    <MudDivider Class="my-2" />
    <MudNavGroup Title="Tags" Icon="@Icons.Material.Filled.Label" Expanded="false">
        @if (_tags is null)
        {
            <MudProgressLinear Indeterminate="true" Class="my-2" />
        }
        else if (_tags.Count == 0)
        {
            <MudText Typo="Typo.body2" Class="px-4 py-1 mud-text-secondary">No tags yet</MudText>
        }
        else
        {
            @foreach (var t in _tags)
            {
                <MudNavLink Href="@($"/items?tag={Uri.EscapeDataString(t.Name)}")"
                            Icon="@Icons.Material.Filled.Label">
                    @t.Name (@t.ItemCount)
                </MudNavLink>
            }
        }
        <MudNavLink Href="/tags" Icon="@Icons.Material.Filled.MoreHoriz">View all tags</MudNavLink>
    </MudNavGroup>

    <MudDivider Class="my-2" />
    <MudNavLink Href="/items/new" Icon="@Icons.Material.Filled.Add">New Item</MudNavLink>

    <MudDivider Class="my-2" />
    @if (!string.IsNullOrEmpty(_email))
    {
        <MudText Typo="Typo.body2" Class="px-4 py-1 mud-text-secondary">@_email</MudText>
    }
    <MudNavLink Href="/Account/Logout" Icon="@Icons.Material.Filled.Logout">Sign out</MudNavLink>
</MudNavMenu>

@code {
    private List<TagSummary>? _tags;
    private string? _email;

    protected override async Task OnInitializedAsync()
    {
        var state = await AuthProvider.GetAuthenticationStateAsync();
        _email = state.User.Identity?.Name;

        if (state.User.Identity?.IsAuthenticated == true)
        {
            _tags = await TagService.GetTagSummaryAsync();
        }
        else
        {
            _tags = new List<TagSummary>();
        }

        Nav.LocationChanged += OnLocationChanged;
    }

    private async void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        // Refresh tag counts as the user navigates (cheap query).
        var state = await AuthProvider.GetAuthenticationStateAsync();
        if (state.User.Identity?.IsAuthenticated == true)
        {
            _tags = await TagService.GetTagSummaryAsync();
            await InvokeAsync(StateHasChanged);
        }
    }

    public void Dispose() => Nav.LocationChanged -= OnLocationChanged;
}
```

- [ ] **Step 4: Build**

Run: `dotnet build`
Expected: succeeds.

- [ ] **Step 5: Manual verification**

Run: `dotnet watch`, sign in as demo.
1. The nav drawer shows a **Tags** group; expand it — `work (1)`, `personal (1)`, `ideas (1)`.
2. Click `work`. Expected: `/items?tag=work` shows only the work-tagged item.
3. The account section shows `demo@nook.local` and a Sign out link that works.
Stop the app.

- [ ] **Step 6: Commit**

```bash
git add Components/Layout/NavMenu.razor Components/Pages/Items.razor
git commit -m "feat: nav menu tags group, new links, account section; tag-name filtering"
```

---

## Task 10: Activity Log page

**Files:**
- Create: `Components/Pages/Log.razor`

**Interfaces:**
- Consumes: `IActivityService.GetForUserAsync(...)`, `ICurrentUser.GetRequiredUserIdAsync()`.
- Produces: route `/log` — a filterable, newest-first table of the user's activity.

- [ ] **Step 1: Create the Log page**

Create `Components/Pages/Log.razor`:

```razor
@page "/log"
@inject IActivityService ActivityService
@inject ICurrentUser CurrentUser

<PageTitle>Activity Log · Nook</PageTitle>

<MudText Typo="Typo.h4" GutterBottom="true">Activity Log</MudText>

<MudPaper Class="pa-4 mb-4 d-flex gap-4 align-center flex-wrap" Elevation="0">
    <MudSelect T="ActivityType?" Label="Type" Value="_type" ValueChanged="OnTypeChanged"
               Clearable="true" Dense="true" Style="min-width:180px;">
        @foreach (var t in Enum.GetValues<ActivityType>())
        {
            <MudSelectItem T="ActivityType?" Value="@t">@t</MudSelectItem>
        }
    </MudSelect>
</MudPaper>

@if (_rows is null)
{
    <MudProgressCircular Indeterminate="true" />
}
else if (_rows.Count == 0)
{
    <MudAlert Severity="Severity.Info">No activity yet.</MudAlert>
}
else
{
    <MudTable Items="_rows" Dense="true" Hover="true" Breakpoint="Breakpoint.Sm">
        <HeaderContent>
            <MudTh>When</MudTh>
            <MudTh>Action</MudTh>
            <MudTh>Item</MudTh>
            <MudTh>Detail</MudTh>
        </HeaderContent>
        <RowTemplate>
            <MudTd DataLabel="When">@context.Timestamp.ToLocalTime().ToString("g")</MudTd>
            <MudTd DataLabel="Action">
                <MudChip T="string" Size="Size.Small" Color="@ColorFor(context.Type)">@context.Type</MudChip>
            </MudTd>
            <MudTd DataLabel="Item">
                @if (context.ItemId is int id)
                {
                    <MudLink Href="@($"/items/{id}")">@context.ItemTitle</MudLink>
                }
                else
                {
                    @context.ItemTitle
                }
            </MudTd>
            <MudTd DataLabel="Detail">@context.Detail</MudTd>
        </RowTemplate>
    </MudTable>
}

@code {
    private List<ActivityLog>? _rows;
    private ActivityType? _type;

    protected override Task OnInitializedAsync() => LoadAsync();

    private async Task OnTypeChanged(ActivityType? type)
    {
        _type = type;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var userId = await CurrentUser.GetRequiredUserIdAsync();
        _rows = await ActivityService.GetForUserAsync(userId, _type, take: 500);
    }

    private static Color ColorFor(ActivityType type) => type switch
    {
        ActivityType.Created => Color.Success,
        ActivityType.Completed => Color.Primary,
        ActivityType.Deleted => Color.Error,
        ActivityType.Archived => Color.Warning,
        _ => Color.Default
    };
}
```

> Confirm the item-detail route is `/items/{id}` by checking `Components/Pages/ItemDetail.razor`'s `@page`. Adjust the `Href` if it differs.

- [ ] **Step 2: Build**

Run: `dotnet build`
Expected: succeeds.

- [ ] **Step 3: Manual verification**

Run: `dotnet watch`, sign in as demo. Create an item, complete a todo, then visit `/log`.
Expected: rows for Created/Completed appear newest-first; the Type filter narrows them; item links navigate to the item.
Stop the app.

- [ ] **Step 4: Commit**

```bash
git add Components/Pages/Log.razor
git commit -m "feat: activity log page"
```

---

## Task 11: Timeline service (shoutouts) and Timeline page

**Files:**
- Create: `Services/TimelineModels.cs`
- Create: `Services/ITimelineService.cs`
- Create: `Services/TimelineService.cs`
- Create: `Nook.Tests/TimelineServiceTests.cs`
- Create: `Components/Pages/Timeline.razor`
- Modify: `Program.cs`

**Interfaces:**
- Produces:
  - `TimelineEntry` (abstract) with `DayEntry(DateOnly Date, IReadOnlyList<ActivityLog> Events)` and `ShoutoutEntry(string Text, string Icon)`.
  - `ITimelineService.BuildAsync(string userId)` → `Task<List<TimelineEntry>>` — day groups newest-first with a shoutout inserted before each ISO-week boundary.
  - Pure helper `TimelineService.GenerateWeekShoutouts(IReadOnlyList<ActivityLog> weekEvents)` → `List<ShoutoutEntry>` (deterministic, unit-tested).

- [ ] **Step 1: Define timeline models**

Create `Services/TimelineModels.cs`:

```csharp
using Nook.Models;

namespace Nook.Services;

/// <summary>An entry rendered on the timeline: either a day of events or a shoutout card.</summary>
public abstract record TimelineEntry;

public sealed record DayEntry(DateOnly Date, IReadOnlyList<ActivityLog> Events) : TimelineEntry;

public sealed record ShoutoutEntry(string Text, string Icon) : TimelineEntry;
```

- [ ] **Step 2: Define the interface**

Create `Services/ITimelineService.cs`:

```csharp
namespace Nook.Services;

/// <summary>Builds the timeline (day groups interleaved with shoutout cards) for a user.</summary>
public interface ITimelineService
{
    Task<List<TimelineEntry>> BuildAsync(string userId);
}
```

- [ ] **Step 3: Write failing tests for shoutout generation**

Create `Nook.Tests/TimelineServiceTests.cs`:

```csharp
using Nook.Models;
using Nook.Services;
using Xunit;

namespace Nook.Tests;

public class TimelineServiceTests
{
    private static ActivityLog Log(ActivityType type, DateTime ts) =>
        new() { UserId = "u", Type = type, ItemTitle = "x", Timestamp = ts };

    [Fact]
    public void GenerateWeekShoutouts_counts_completed_todos()
    {
        var monday = new DateTime(2026, 6, 8, 9, 0, 0, DateTimeKind.Utc);
        var events = new List<ActivityLog>
        {
            Log(ActivityType.Completed, monday),
            Log(ActivityType.Completed, monday.AddHours(2)),
            Log(ActivityType.Created, monday.AddDays(1)),
        };

        var shoutouts = TimelineService.GenerateWeekShoutouts(events);

        Assert.Contains(shoutouts, s => s.Text.Contains("2") && s.Text.Contains("completed"));
    }

    [Fact]
    public void GenerateWeekShoutouts_reports_busiest_day()
    {
        var monday = new DateTime(2026, 6, 8, 9, 0, 0, DateTimeKind.Utc);
        var tuesday = monday.AddDays(1);
        var events = new List<ActivityLog>
        {
            Log(ActivityType.Created, monday),
            Log(ActivityType.Created, tuesday),
            Log(ActivityType.Created, tuesday.AddHours(1)),
            Log(ActivityType.Created, tuesday.AddHours(2)),
        };

        var shoutouts = TimelineService.GenerateWeekShoutouts(events);

        Assert.Contains(shoutouts, s => s.Text.Contains("Tuesday"));
    }

    [Fact]
    public void GenerateWeekShoutouts_returns_empty_for_no_events()
    {
        var shoutouts = TimelineService.GenerateWeekShoutouts(new List<ActivityLog>());
        Assert.Empty(shoutouts);
    }
}
```

- [ ] **Step 4: Run to confirm failure**

Run: `dotnet test Nook.Tests/Nook.Tests.csproj`
Expected: FAIL — `TimelineService` does not exist.

- [ ] **Step 5: Implement `TimelineService`**

Create `Services/TimelineService.cs`:

```csharp
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Nook.Data;
using Nook.Models;

namespace Nook.Services;

public sealed class TimelineService : ITimelineService
{
    private readonly IDbContextFactory<NookContext> _factory;

    public TimelineService(IDbContextFactory<NookContext> factory)
    {
        _factory = factory;
    }

    public async Task<List<TimelineEntry>> BuildAsync(string userId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var events = await db.ActivityLogs
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.Timestamp)
            .Take(500)
            .ToListAsync();

        var entries = new List<TimelineEntry>();
        if (events.Count == 0) return entries;

        // Group by day (newest-first), and emit week shoutouts when crossing week boundaries.
        var byDay = events
            .GroupBy(e => DateOnly.FromDateTime(e.Timestamp.ToLocalTime()))
            .OrderByDescending(g => g.Key);

        int? currentWeek = null;
        var weekBuffer = new List<ActivityLog>();

        void FlushWeek()
        {
            if (weekBuffer.Count > 0)
            {
                foreach (var shoutout in GenerateWeekShoutouts(weekBuffer))
                    entries.Add(shoutout);
                weekBuffer.Clear();
            }
        }

        foreach (var day in byDay)
        {
            var week = IsoWeek(day.Key);
            if (currentWeek is not null && week != currentWeek)
            {
                FlushWeek();
            }
            currentWeek = week;
            weekBuffer.AddRange(day);
            entries.Add(new DayEntry(day.Key, day.OrderByDescending(e => e.Timestamp).ToList()));
        }
        FlushWeek();

        return entries;
    }

    private static int IsoWeek(DateOnly date) =>
        ISOWeek.GetWeekOfYear(date.ToDateTime(TimeOnly.MinValue)) + ISOWeek.GetYear(date.ToDateTime(TimeOnly.MinValue)) * 100;

    /// <summary>
    /// Deterministic shoutouts summarizing one week's events. Pure (no I/O) for testability.
    /// </summary>
    public static List<ShoutoutEntry> GenerateWeekShoutouts(IReadOnlyList<ActivityLog> weekEvents)
    {
        var shoutouts = new List<ShoutoutEntry>();
        if (weekEvents.Count == 0) return shoutouts;

        var completed = weekEvents.Count(e => e.Type == ActivityType.Completed);
        if (completed > 0)
        {
            shoutouts.Add(new ShoutoutEntry(
                $"{completed} item{(completed == 1 ? "" : "s")} completed this week 🎉",
                "Celebration"));
        }

        var created = weekEvents.Count(e => e.Type == ActivityType.Created);
        if (created > 0)
        {
            shoutouts.Add(new ShoutoutEntry(
                $"{created} new item{(created == 1 ? "" : "s")} captured",
                "NoteAdd"));
        }

        // Busiest day of the week.
        var busiest = weekEvents
            .GroupBy(e => e.Timestamp.ToLocalTime().DayOfWeek)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .First();
        if (busiest.Count() >= 2)
        {
            shoutouts.Add(new ShoutoutEntry(
                $"Most productive day: {busiest.Key} ({busiest.Count()} events)",
                "TrendingUp"));
        }

        return shoutouts;
    }
}
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test Nook.Tests/Nook.Tests.csproj`
Expected: PASS (all, including 3 new timeline tests).

- [ ] **Step 7: Register the service**

In `Program.cs`, add:

```csharp
builder.Services.AddScoped<ITimelineService, TimelineService>();
```

- [ ] **Step 8: Create the Timeline page**

Create `Components/Pages/Timeline.razor`:

```razor
@page "/timeline"
@inject ITimelineService TimelineService
@inject ICurrentUser CurrentUser

<PageTitle>Timeline · Nook</PageTitle>

<MudText Typo="Typo.h4" GutterBottom="true">Timeline</MudText>

@if (_entries is null)
{
    <MudProgressCircular Indeterminate="true" />
}
else if (_entries.Count == 0)
{
    <MudAlert Severity="Severity.Info">Your timeline is empty. Start capturing items!</MudAlert>
}
else
{
    <MudTimeline TimelinePosition="TimelinePosition.Start">
        @foreach (var entry in _entries)
        {
            @if (entry is ShoutoutEntry shoutout)
            {
                <MudTimelineItem Color="Color.Secondary" Icon="@IconFor(shoutout.Icon)">
                    <MudPaper Class="pa-3" Elevation="2">
                        <MudText Typo="Typo.subtitle1">@shoutout.Text</MudText>
                    </MudPaper>
                </MudTimelineItem>
            }
            else if (entry is DayEntry day)
            {
                <MudTimelineItem Color="Color.Primary" Icon="@Icons.Material.Filled.Event">
                    <MudText Typo="Typo.subtitle2">@day.Date.ToString("dddd, MMM d")</MudText>
                    @foreach (var ev in day.Events)
                    {
                        <MudText Typo="Typo.body2" Class="mud-text-secondary">
                            @ev.Type — @ev.ItemTitle
                        </MudText>
                    }
                </MudTimelineItem>
            }
        }
    </MudTimeline>
}

@code {
    private List<TimelineEntry>? _entries;

    protected override async Task OnInitializedAsync()
    {
        var userId = await CurrentUser.GetRequiredUserIdAsync();
        _entries = await TimelineService.BuildAsync(userId);
    }

    private static string IconFor(string name) => name switch
    {
        "Celebration" => Icons.Material.Filled.Celebration,
        "NoteAdd" => Icons.Material.Filled.NoteAdd,
        "TrendingUp" => Icons.Material.Filled.TrendingUp,
        _ => Icons.Material.Filled.Star
    };
}
```

- [ ] **Step 9: Build and manually verify**

Run: `dotnet build` (expected: succeeds), then `dotnet watch`, sign in as demo, complete the seeded todo and create a couple items, visit `/timeline`.
Expected: day entries newest-first with shoutout cards (e.g. "1 item completed this week 🎉"). Stop the app.

- [ ] **Step 10: Commit**

```bash
git add Services/TimelineModels.cs Services/ITimelineService.cs Services/TimelineService.cs Nook.Tests/TimelineServiceTests.cs Components/Pages/Timeline.razor Program.cs
git commit -m "feat: timeline service with shoutouts and timeline page"
```

---

## Task 12: Analytics service and Analytics page

**Files:**
- Create: `Services/AnalyticsModels.cs`
- Create: `Services/IAnalyticsService.cs`
- Create: `Services/AnalyticsService.cs`
- Create: `Nook.Tests/AnalyticsServiceTests.cs`
- Create: `Components/Pages/Analytics.razor`
- Modify: `Program.cs`

**Interfaces:**
- Produces:
  - `AnalyticsModel` record (counts, trends, productivity, tag insights).
  - `IAnalyticsService.GetForUserAsync(string userId)` → `Task<AnalyticsModel>`.

- [ ] **Step 1: Define the analytics model**

Create `Services/AnalyticsModels.cs`:

```csharp
using Nook.Models;

namespace Nook.Services;

public sealed record CountSlice(string Label, int Count);

public sealed record WeekPoint(DateOnly WeekStart, int Created, int Completed);

public sealed record AnalyticsModel(
    int TotalItems,
    int OpenItems,
    int CompletedItems,
    double CompletionRatePercent,
    int OverdueCount,
    int UntaggedCount,
    DayOfWeek? BusiestDay,
    IReadOnlyList<CountSlice> ByType,
    IReadOnlyList<CountSlice> ByStatus,
    IReadOnlyList<CountSlice> ByPriority,
    IReadOnlyList<CountSlice> TopTags,
    IReadOnlyList<WeekPoint> WeeklyTrend);
```

- [ ] **Step 2: Define the interface**

Create `Services/IAnalyticsService.cs`:

```csharp
namespace Nook.Services;

/// <summary>Computes per-user analytics for the analytics dashboard.</summary>
public interface IAnalyticsService
{
    Task<AnalyticsModel> GetForUserAsync(string userId);
}
```

- [ ] **Step 3: Write failing tests**

Create `Nook.Tests/AnalyticsServiceTests.cs`:

```csharp
using Nook.Models;
using Nook.Services;
using Xunit;

namespace Nook.Tests;

public class AnalyticsServiceTests
{
    private static async Task SeedAsync(TestDbContextFactory factory)
    {
        await using var db = factory.CreateDbContext();
        db.Items.AddRange(
            new Item { Title = "n1", ItemType = ItemType.Note, Status = ItemStatus.Open, UserId = "u" },
            new Item { Title = "t1", ItemType = ItemType.Todo, Status = ItemStatus.Done, UserId = "u",
                       CompletedDate = DateTime.UtcNow },
            new Item { Title = "t2", ItemType = ItemType.Todo, Status = ItemStatus.Done, UserId = "u",
                       CompletedDate = DateTime.UtcNow },
            // Another user's item must be ignored.
            new Item { Title = "x", ItemType = ItemType.Note, Status = ItemStatus.Open, UserId = "other" });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetForUserAsync_counts_only_that_users_items()
    {
        var factory = new TestDbContextFactory();
        await SeedAsync(factory);
        var sut = new AnalyticsService(factory);

        var model = await sut.GetForUserAsync("u");

        Assert.Equal(3, model.TotalItems);
        Assert.Equal(2, model.CompletedItems);
        Assert.Equal(1, model.OpenItems);
    }

    [Fact]
    public async Task GetForUserAsync_computes_completion_rate()
    {
        var factory = new TestDbContextFactory();
        await SeedAsync(factory);
        var sut = new AnalyticsService(factory);

        var model = await sut.GetForUserAsync("u");

        // 2 of 3 completed ≈ 66.7%.
        Assert.True(model.CompletionRatePercent > 66 && model.CompletionRatePercent < 67);
    }
}
```

- [ ] **Step 4: Run to confirm failure**

Run: `dotnet test Nook.Tests/Nook.Tests.csproj`
Expected: FAIL — `AnalyticsService` does not exist.

- [ ] **Step 5: Implement `AnalyticsService`**

Create `Services/AnalyticsService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Nook.Data;
using Nook.Models;

namespace Nook.Services;

public sealed class AnalyticsService : IAnalyticsService
{
    private readonly IDbContextFactory<NookContext> _factory;

    public AnalyticsService(IDbContextFactory<NookContext> factory)
    {
        _factory = factory;
    }

    public async Task<AnalyticsModel> GetForUserAsync(string userId)
    {
        await using var db = await _factory.CreateDbContextAsync();

        var items = await db.Items
            .Where(i => i.UserId == userId)
            .Select(i => new
            {
                i.ItemType, i.Status, i.Priority, i.DueDate, i.CreatedAt, i.CompletedDate,
                TagCount = i.ItemTags.Count
            })
            .ToListAsync();

        var total = items.Count;
        var completed = items.Count(i => i.Status == ItemStatus.Done);
        var open = items.Count(i => i.Status != ItemStatus.Done);
        var now = DateTime.UtcNow;
        var overdue = items.Count(i => i.Status != ItemStatus.Done && i.DueDate != null && i.DueDate < now);
        var untagged = items.Count(i => i.TagCount == 0);
        var completionRate = total == 0 ? 0 : Math.Round(completed * 100.0 / total, 1);

        var byType = items.GroupBy(i => i.ItemType)
            .Select(g => new CountSlice(g.Key.ToString(), g.Count()))
            .OrderByDescending(s => s.Count).ToList();
        var byStatus = items.GroupBy(i => i.Status)
            .Select(g => new CountSlice(g.Key.ToString(), g.Count()))
            .OrderByDescending(s => s.Count).ToList();
        var byPriority = items.Where(i => i.Priority != null)
            .GroupBy(i => i.Priority!.Value)
            .Select(g => new CountSlice(g.Key.ToString(), g.Count()))
            .OrderByDescending(s => s.Count).ToList();

        // Tag insights.
        var topTags = await db.Tags
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.ItemTags.Count)
            .ThenBy(t => t.Name)
            .Take(10)
            .Select(t => new CountSlice(t.Name, t.ItemTags.Count))
            .ToListAsync();

        // Busiest day-of-week by item creation.
        DayOfWeek? busiest = items.Count == 0 ? null :
            items.GroupBy(i => i.CreatedAt.DayOfWeek)
                 .OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
                 .First().Key;

        // Weekly trend over the last 8 ISO weeks.
        var weekly = BuildWeeklyTrend(
            items.Select(i => (i.CreatedAt, i.CompletedDate)).ToList(), now, weeks: 8);

        return new AnalyticsModel(
            total, open, completed, completionRate, overdue, untagged, busiest,
            byType, byStatus, byPriority, topTags, weekly);
    }

    private static List<WeekPoint> BuildWeeklyTrend(
        List<(DateTime Created, DateTime? Completed)> items, DateTime now, int weeks)
    {
        var points = new List<WeekPoint>();
        // Monday of the current week.
        var today = DateOnly.FromDateTime(now);
        int offset = ((int)today.DayOfWeek + 6) % 7; // Monday=0
        var currentMonday = today.AddDays(-offset);

        for (int w = weeks - 1; w >= 0; w--)
        {
            var start = currentMonday.AddDays(-7 * w);
            var end = start.AddDays(7);
            var created = items.Count(i =>
            {
                var d = DateOnly.FromDateTime(i.Created);
                return d >= start && d < end;
            });
            var completed = items.Count(i =>
                i.Completed is DateTime c &&
                DateOnly.FromDateTime(c) >= start && DateOnly.FromDateTime(c) < end);
            points.Add(new WeekPoint(start, created, completed));
        }
        return points;
    }
}
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test Nook.Tests/Nook.Tests.csproj`
Expected: PASS (all, including 2 new analytics tests).

- [ ] **Step 7: Register the service**

In `Program.cs`, add:

```csharp
builder.Services.AddScoped<IAnalyticsService, AnalyticsService>();
```

- [ ] **Step 8: Create the Analytics page**

Create `Components/Pages/Analytics.razor`:

```razor
@page "/analytics"
@inject IAnalyticsService AnalyticsService
@inject ICurrentUser CurrentUser

<PageTitle>Analytics · Nook</PageTitle>

<MudText Typo="Typo.h4" GutterBottom="true">Analytics</MudText>

@if (_model is null)
{
    <MudProgressCircular Indeterminate="true" />
}
else
{
    <MudGrid>
        <MudItem xs="6" md="3">@StatCard("Total items", _model.TotalItems.ToString())</MudItem>
        <MudItem xs="6" md="3">@StatCard("Completed", _model.CompletedItems.ToString())</MudItem>
        <MudItem xs="6" md="3">@StatCard("Completion rate", $"{_model.CompletionRatePercent}%")</MudItem>
        <MudItem xs="6" md="3">@StatCard("Overdue", _model.OverdueCount.ToString())</MudItem>
    </MudGrid>

    <MudGrid Class="mt-2">
        <MudItem xs="12" md="6">
            <MudPaper Class="pa-4">
                <MudText Typo="Typo.h6" GutterBottom="true">Items by type</MudText>
                @if (_model.ByType.Count > 0)
                {
                    <MudChart ChartType="ChartType.Pie"
                              InputData="@_model.ByType.Select(s => (double)s.Count).ToArray()"
                              InputLabels="@_model.ByType.Select(s => s.Label).ToArray()" Width="100%" Height="260px" />
                }
                else { <MudText Typo="Typo.body2">No data.</MudText> }
            </MudPaper>
        </MudItem>
        <MudItem xs="12" md="6">
            <MudPaper Class="pa-4">
                <MudText Typo="Typo.h6" GutterBottom="true">Items by status</MudText>
                @if (_model.ByStatus.Count > 0)
                {
                    <MudChart ChartType="ChartType.Donut"
                              InputData="@_model.ByStatus.Select(s => (double)s.Count).ToArray()"
                              InputLabels="@_model.ByStatus.Select(s => s.Label).ToArray()" Width="100%" Height="260px" />
                }
                else { <MudText Typo="Typo.body2">No data.</MudText> }
            </MudPaper>
        </MudItem>
        <MudItem xs="12">
            <MudPaper Class="pa-4">
                <MudText Typo="Typo.h6" GutterBottom="true">Weekly activity (created vs completed)</MudText>
                <MudChart ChartType="ChartType.Line"
                          ChartSeries="@_trendSeries"
                          XAxisLabels="@_model.WeeklyTrend.Select(p => p.WeekStart.ToString("M/d")).ToArray()"
                          Width="100%" Height="300px" />
            </MudPaper>
        </MudItem>
        <MudItem xs="12" md="6">
            <MudPaper Class="pa-4">
                <MudText Typo="Typo.h6" GutterBottom="true">Top tags</MudText>
                @if (_model.TopTags.Count > 0)
                {
                    <MudSimpleTable Dense="true">
                        <tbody>
                            @foreach (var t in _model.TopTags)
                            {
                                <tr><td>@t.Label</td><td style="text-align:right">@t.Count</td></tr>
                            }
                        </tbody>
                    </MudSimpleTable>
                }
                else { <MudText Typo="Typo.body2">No tags yet.</MudText> }
            </MudPaper>
        </MudItem>
        <MudItem xs="12" md="6">
            <MudPaper Class="pa-4">
                <MudText Typo="Typo.h6" GutterBottom="true">Productivity</MudText>
                <MudText Typo="Typo.body1">Busiest day: @(_model.BusiestDay?.ToString() ?? "—")</MudText>
                <MudText Typo="Typo.body1">Untagged items: @_model.UntaggedCount</MudText>
                <MudText Typo="Typo.body1">Open items: @_model.OpenItems</MudText>
            </MudPaper>
        </MudItem>
    </MudGrid>
}

@code {
    private AnalyticsModel? _model;
    private List<ChartSeries> _trendSeries = new();

    protected override async Task OnInitializedAsync()
    {
        var userId = await CurrentUser.GetRequiredUserIdAsync();
        _model = await AnalyticsService.GetForUserAsync(userId);
        _trendSeries = new List<ChartSeries>
        {
            new() { Name = "Created", Data = _model.WeeklyTrend.Select(p => (double)p.Created).ToArray() },
            new() { Name = "Completed", Data = _model.WeeklyTrend.Select(p => (double)p.Completed).ToArray() }
        };
    }

    private RenderFragment StatCard(string label, string value) =>@<MudPaper Class="pa-4 text-center">
        <MudText Typo="Typo.h4">@value</MudText>
        <MudText Typo="Typo.body2" Class="mud-text-secondary">@label</MudText>
    </MudPaper>;
}
```

- [ ] **Step 9: Build and manually verify**

Run: `dotnet build` (expected: succeeds), then `dotnet watch`, sign in as demo, visit `/analytics`.
Expected: stat cards, pie/donut charts, the weekly line chart, top-tags table, and productivity panel all render with the seeded data and no prerender query errors. Stop the app.

- [ ] **Step 10: Commit**

```bash
git add Services/AnalyticsModels.cs Services/IAnalyticsService.cs Services/AnalyticsService.cs Nook.Tests/AnalyticsServiceTests.cs Components/Pages/Analytics.razor Program.cs
git commit -m "feat: analytics service and analytics dashboard page"
```

---

## Final verification

- [ ] **Run the whole test suite**

Run: `dotnet test`
Expected: all tests PASS.

- [ ] **Smoke-test every page while signed in**

Run: `dotnet watch`, register a brand-new user (proves isolation: the new account sees an empty app, not the demo data). Then sign in as demo and load, in turn: `/` → redirects to `/dashboard`, `/items`, `/items?tag=work`, `/todos`, `/reminders`, `/bookmarks`, `/tags`, `/timeline`, `/analytics`, `/log`, `/archive`, and `/items/new`. Confirm none throw and the nav menu's Tags group and account section render.

- [ ] **Confirm sign-out**

From any page, use the nav menu **Sign out** → lands on `/`, and visiting `/dashboard` now redirects to `/login`.

---

## Notes on test strategy

- **Service logic** (activity logging, user scoping, shoutout generation, analytics aggregation) is covered by xUnit tests against EF Core InMemory — that's where the real logic lives.
- **Razor pages / auth flow** are verified by `dotnet build` + manual load steps, consistent with the project's known gotcha that some EF translation issues only surface on prerender. InMemory does not catch SQL-translation problems, so the manual page loads are the safety net for the `OrderBy`-before-`Select` rule.
- If you later want automated page tests, add bUnit — out of scope here.
