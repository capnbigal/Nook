using Microsoft.EntityFrameworkCore;
using Nook.Data;
using Nook.Models;

namespace Nook.Services;

/// <summary>Default <see cref="ITagService"/> implementation.</summary>
public class TagService : ITagService
{
    private readonly IDbContextFactory<NookContext> _factory;

    public TagService(IDbContextFactory<NookContext> factory)
    {
        _factory = factory;
    }

    public async Task<List<Tag>> GetAllAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Tags.OrderBy(t => t.Name).ToListAsync();
    }

    public async Task<Tag?> GetByIdAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Tags.FindAsync(id);
    }

    public async Task<Tag> CreateAsync(string name, string? color = null)
    {
        name = name.Trim();
        await using var db = await _factory.CreateDbContextAsync();
        // Name comparison is case-insensitive under SQL Server's default collation.
        if (await db.Tags.AnyAsync(t => t.Name == name))
        {
            throw new InvalidOperationException($"A tag named \"{name}\" already exists.");
        }
        var tag = new Tag { Name = name, Color = color };
        db.Tags.Add(tag);
        await db.SaveChangesAsync();
        return tag;
    }

    public async Task<Tag> GetOrCreateAsync(string name, string? color = null)
    {
        name = name.Trim();
        await using var db = await _factory.CreateDbContextAsync();
        var existing = await db.Tags.FirstOrDefaultAsync(t => t.Name == name);
        if (existing is not null) return existing;

        var tag = new Tag { Name = name, Color = color };
        db.Tags.Add(tag);
        await db.SaveChangesAsync();
        return tag;
    }

    public async Task UpdateAsync(Tag tag)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var existing = await db.Tags.FindAsync(tag.TagId);
        if (existing is null) return;
        existing.Name = tag.Name.Trim();
        existing.Color = tag.Color;
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var tag = await db.Tags.FindAsync(id);
        if (tag is null) return;
        db.Tags.Remove(tag); // ItemTags cascade away with the tag.
        await db.SaveChangesAsync();
    }

    public async Task<List<TagSummary>> GetTagSummaryAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        // Order on the server using the underlying expressions, then project.
        // Ordering by a property of the projected TagSummary record can't be translated to SQL.
        return await db.Tags
            .OrderByDescending(t => t.ItemTags.Count)
            .ThenBy(t => t.Name)
            .Select(t => new TagSummary(t.TagId, t.Name, t.Color, t.ItemTags.Count))
            .ToListAsync();
    }

    public async Task AssignTagAsync(int itemId, int tagId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var exists = await db.ItemTags.AnyAsync(it => it.ItemId == itemId && it.TagId == tagId);
        if (exists) return;
        db.ItemTags.Add(new ItemTag { ItemId = itemId, TagId = tagId });
        await db.SaveChangesAsync();
    }

    public async Task RemoveTagAsync(int itemId, int tagId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var link = await db.ItemTags.FirstOrDefaultAsync(it => it.ItemId == itemId && it.TagId == tagId);
        if (link is null) return;
        db.ItemTags.Remove(link);
        await db.SaveChangesAsync();
    }
}
