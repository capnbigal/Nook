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
