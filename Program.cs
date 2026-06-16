using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using Nook.Components;
using Nook.Data;
using Nook.Services;

var builder = WebApplication.CreateBuilder(args);

// Razor components with Interactive Server rendering.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// MudBlazor UI services.
builder.Services.AddMudServices();

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

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Apply migrations and seed starter data on startup (Development only).
// For production, prefer applying migrations as an explicit deployment step.
if (app.Environment.IsDevelopment())
{
    await DbSeeder.InitializeAsync(app.Services);
}

app.Run();
