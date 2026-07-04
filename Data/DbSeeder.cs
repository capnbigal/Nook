using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Nook.Models;
using Nook.Services;

namespace Nook.Data;

/// <summary>
/// Applies pending schema migrations and seeds system reference data (relation
/// types + verbs) on every start — both idempotent and non-destructive. On a
/// brand-new (empty) database it also creates a demo user and a small set of
/// starter graph nodes. It never runs the legacy Item→Node backfill: that is an
/// explicit, deliberate operation (see the /admin/graph-migration page).
/// </summary>
public static class DbSeeder
{
    public const string DemoEmail = "demo@nook.local";
    public const string DemoPassword = "Demo123!";

    public static async Task InitializeAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;

        var factory = sp.GetRequiredService<IDbContextFactory<NookContext>>();
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.MigrateAsync();

        // Always seed system relation types + verbs (idempotent, non-destructive).
        await sp.GetRequiredService<IGraphMigrationService>().SeedSystemDataAsync();

        // Only seed demo content into a genuinely empty database (no graph and no legacy data).
        if (await db.Nodes.AnyAsync() || await db.Items.AnyAsync())
            return;

        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var demo = await userManager.FindByEmailAsync(DemoEmail);
        if (demo is null)
        {
            demo = new ApplicationUser { UserName = DemoEmail, Email = DemoEmail, EmailConfirmed = true };
            var result = await userManager.CreateAsync(demo, DemoPassword);
            if (!result.Succeeded)
                throw new InvalidOperationException(
                    "Failed to create demo user: " + string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        var uid = demo.Id;
        var personal = new Tag { Name = "personal", Color = "#43A047", UserId = uid };
        var reading = new Tag { Name = "reading", Color = "#1E88E5", UserId = uid };
        db.Tags.AddRange(personal, reading);

        var welcome = new Node
        {
            UserId = uid, Kind = NodeKind.Note, State = NodeState.Active, IsPinned = true,
            Title = "Welcome to Nook",
            Body = "Capture anything as a node, then connect it. People, projects, notes, "
                 + "bookmarks and events all live in one graph. Use the + button to capture, "
                 + "then promote, tag, relate, collect, or attach actions when you're ready.",
            NodeTags = new List<NodeTag> { new() { Tag = personal } },
        };
        var jamie = new Node
        {
            UserId = uid, Kind = NodeKind.Person, State = NodeState.Active, Title = "Jamie",
            Body = "A friend. Try relating notes to Jamie, or make a queue of things to tell them.",
        };
        var article = new Node
        {
            UserId = uid, Kind = NodeKind.Bookmark, State = NodeState.Active,
            Title = "MudBlazor documentation", Url = "https://mudblazor.com",
            NodeTags = new List<NodeTag> { new() { Tag = reading } },
        };
        db.Nodes.AddRange(welcome, jamie, article);
        await db.SaveChangesAsync();

        // A "Read" task attached to the bookmark, showing action-on-any-node.
        db.ActionItems.Add(new ActionItem
        {
            UserId = uid, Kind = ActionKind.Task, Status = ActionStatus.Open,
            Verb = ActionVerb.Read, Title = "Read the MudBlazor docs",
            TargetNodeId = article.NodeId, DueDate = DateTime.UtcNow.AddDays(3),
        });
        await db.SaveChangesAsync();
    }
}
