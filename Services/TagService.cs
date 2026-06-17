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
