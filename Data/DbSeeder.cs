using Microsoft.EntityFrameworkCore;
using Nook.Models;

namespace Nook.Data;

/// <summary>
/// Applies any pending migrations and seeds a little starter data so the UI
/// isn't empty on first run. Intended for Development; for production you would
/// typically apply migrations as a separate deployment step.
/// </summary>
public static class DbSeeder
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        var factory = services.GetRequiredService<IDbContextFactory<NookContext>>();
        await using var db = await factory.CreateDbContextAsync();

        await db.Database.MigrateAsync();

        // Only seed an empty database.
        if (await db.Tags.AnyAsync() || await db.Items.AnyAsync())
        {
            return;
        }

        var work = new Tag { Name = "work", Color = "#1E88E5" };
        var personal = new Tag { Name = "personal", Color = "#43A047" };
        var ideas = new Tag { Name = "ideas", Color = "#8E24AA" };
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
                ItemTags = new List<ItemTag> { new() { Tag = work } }
            },
            new Item
            {
                Title = "MudBlazor documentation",
                ItemType = ItemType.Bookmark,
                Status = ItemStatus.Open,
                Url = "https://mudblazor.com",
                ItemTags = new List<ItemTag> { new() { Tag = ideas } }
            }
        );

        await db.SaveChangesAsync();
    }
}
