using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using Nook.Components;
using Nook.Components.Account;
using Nook.Data;
using Nook.Models;
using Nook.Services;

var builder = WebApplication.CreateBuilder(args);

// Razor components with Interactive Server rendering.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// MudBlazor UI services.
builder.Services.AddMudServices();

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

// Point the cookie challenge at our custom login page (default is /Account/Login),
// keeping it consistent with the RedirectToLogin component and the /login route.
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/login";
    options.AccessDeniedPath = "/login";
});

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

// EF Core (SQL Server) via a context factory — the recommended pattern for
// Blazor Server, where a circuit-scoped context isn't safe across concurrent renders.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'DefaultConnection' not found. Add it to appsettings.Development.json.");
builder.Services.AddDbContextFactory<NookContext>(options =>
    options.UseSqlServer(connectionString));

// Application services.
builder.Services.AddScoped<IItemService, ItemService>();
builder.Services.AddScoped<ITagService, TagService>();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<IActivityService, ActivityService>();
builder.Services.AddScoped<ITimelineService, TimelineService>();
builder.Services.AddScoped<IAnalyticsService, AnalyticsService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapAdditionalIdentityEndpoints();

// Apply migrations and seed starter data on startup. Runs in every environment
// so the container creates/migrates its database on first start (the seeder is
// idempotent — it only inserts the demo user + sample items into an empty DB).
await DbSeeder.InitializeAsync(app.Services);

app.Run();
