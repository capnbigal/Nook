using Microsoft.EntityFrameworkCore;
using Nook.Data;
using Nook.Models;

namespace Nook.Services;

/// <summary>
/// Default <see cref="IItemService"/> implementation. Each call creates a
/// short-lived <see cref="NookContext"/> from the factory so the service
/// is safe to use across concurrent Blazor Server renders.
/// </summary>
public class ItemService : IItemService
{
    private readonly IDbContextFactory<NookContext> _factory;

    public ItemService(IDbContextFactory<NookContext> factory)
    {
        _factory = factory;
    }

    // Eager-load tags so cards/chips can render without extra round-trips.
    private static IQueryable<Item> WithTags(IQueryable<Item> query) =>
        query.Include(i => i.ItemTags).ThenInclude(it => it.Tag);

    public async Task<List<Item>> GetItemsAsync(ItemFilter filter)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var query = WithTags(db.Items).AsQueryable();

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

        if (filter.ItemType.HasValue)
            query = query.Where(i => i.ItemType == filter.ItemType.Value);
        if (filter.Status.HasValue)
            query = query.Where(i => i.Status == filter.Status.Value);
        if (filter.Priority.HasValue)
            query = query.Where(i => i.Priority == filter.Priority.Value);
        if (filter.TagId.HasValue)
            query = query.Where(i => i.ItemTags.Any(it => it.TagId == filter.TagId.Value));
        if (filter.FavoritesOnly)
            query = query.Where(i => i.IsFavorite);
        if (filter.PinnedOnly)
            query = query.Where(i => i.IsPinned);

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
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Items
            .Include(i => i.ItemTags).ThenInclude(it => it.Tag)
            .Include(i => i.Parent)
            .Include(i => i.Children)
            .Include(i => i.OutgoingLinks).ThenInclude(l => l.TargetItem)
            .Include(i => i.IncomingLinks).ThenInclude(l => l.SourceItem)
            .FirstOrDefaultAsync(i => i.ItemId == id);
    }

    public async Task<Item> CreateAsync(Item item, IEnumerable<int>? tagIds = null)
    {
        await using var db = await _factory.CreateDbContextAsync();
        db.Items.Add(item);
        if (tagIds != null)
        {
            foreach (var tagId in tagIds.Distinct())
            {
                item.ItemTags.Add(new ItemTag { TagId = tagId });
            }
        }
        await db.SaveChangesAsync();
        return item;
    }

    public async Task UpdateAsync(Item item, IEnumerable<int>? tagIds = null)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var existing = await db.Items
            .Include(i => i.ItemTags)
            .FirstOrDefaultAsync(i => i.ItemId == item.ItemId);
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
            {
                existing.ItemTags.Remove(remove);
            }
            var current = existing.ItemTags.Select(it => it.TagId).ToHashSet();
            foreach (var tagId in desired.Where(t => !current.Contains(t)))
            {
                existing.ItemTags.Add(new ItemTag { ItemId = existing.ItemId, TagId = tagId });
            }
        }

        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var item = await db.Items.Include(i => i.Children).FirstOrDefaultAsync(i => i.ItemId == id);
        if (item is null) return;

        // Orphan children rather than cascade-deleting them (FK is Restrict).
        foreach (var child in item.Children)
        {
            child.ParentItemId = null;
        }
        // Remove manual links pointing at this item (FKs are Restrict).
        var links = await db.ItemLinks
            .Where(l => l.SourceItemId == id || l.TargetItemId == id)
            .ToListAsync();
        db.ItemLinks.RemoveRange(links);

        db.Items.Remove(item); // ItemTags cascade away with the item.
        await db.SaveChangesAsync();
    }

    public Task ArchiveAsync(int id) => MutateAsync(id, i => i.ArchivedAt = DateTime.UtcNow);
    public Task UnarchiveAsync(int id) => MutateAsync(id, i => i.ArchivedAt = null);
    public Task TogglePinAsync(int id) => MutateAsync(id, i => i.IsPinned = !i.IsPinned);
    public Task ToggleFavoriteAsync(int id) => MutateAsync(id, i => i.IsFavorite = !i.IsFavorite);

    public Task CompleteAsync(int id) => MutateAsync(id, i =>
    {
        i.Status = ItemStatus.Done;
        i.CompletedDate = DateTime.UtcNow;
    });

    public Task ReopenAsync(int id) => MutateAsync(id, i =>
    {
        i.Status = ItemStatus.Open;
        i.CompletedDate = null;
    });

    private async Task MutateAsync(int id, Action<Item> mutate)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var item = await db.Items.FindAsync(id);
        if (item is null) return;
        mutate(item);
        await db.SaveChangesAsync();
    }

    public async Task<List<Item>> GetRelatedByTagsAsync(int id, int max = 10)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var tagIds = await db.ItemTags
            .Where(it => it.ItemId == id)
            .Select(it => it.TagId)
            .ToListAsync();
        if (tagIds.Count == 0) return new List<Item>();

        return await WithTags(db.Items)
            .Where(i => i.ItemId != id && i.ArchivedAt == null
                        && i.ItemTags.Any(it => tagIds.Contains(it.TagId)))
            .OrderByDescending(i => i.ItemTags.Count(it => tagIds.Contains(it.TagId)))
            .ThenByDescending(i => i.UpdatedAt)
            .Take(max)
            .ToListAsync();
    }

    public async Task<List<Item>> GetChildrenAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await WithTags(db.Items)
            .Where(i => i.ParentItemId == id)
            .OrderBy(i => i.CreatedAt)
            .ToListAsync();
    }

    public async Task LinkAsync(int sourceId, int targetId, string? linkType = null)
    {
        if (sourceId == targetId) return;
        await using var db = await _factory.CreateDbContextAsync();
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

    // ---- Dashboard queries ----

    public async Task<List<Item>> GetRecentlyCreatedAsync(int count = 5)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await WithTags(db.Items)
            .Where(i => i.ArchivedAt == null)
            .OrderByDescending(i => i.CreatedAt)
            .Take(count)
            .ToListAsync();
    }

    public async Task<List<Item>> GetRecentlyUpdatedAsync(int count = 5)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await WithTags(db.Items)
            .Where(i => i.ArchivedAt == null)
            .OrderByDescending(i => i.UpdatedAt)
            .Take(count)
            .ToListAsync();
    }

    public async Task<List<Item>> GetDueSoonAsync(int days = 7, int count = 10)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var now = DateTime.UtcNow;
        var horizon = now.AddDays(days);
        return await WithTags(db.Items)
            .Where(i => i.ArchivedAt == null && i.Status != ItemStatus.Done
                        && i.DueDate != null && i.DueDate >= now && i.DueDate <= horizon)
            .OrderBy(i => i.DueDate)
            .Take(count)
            .ToListAsync();
    }

    public async Task<List<Item>> GetOverdueAsync(int count = 10)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var now = DateTime.UtcNow;
        return await WithTags(db.Items)
            .Where(i => i.ArchivedAt == null && i.Status != ItemStatus.Done
                        && i.DueDate != null && i.DueDate < now)
            .OrderBy(i => i.DueDate)
            .Take(count)
            .ToListAsync();
    }

    public async Task<List<Item>> GetPinnedAsync(int count = 10)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await WithTags(db.Items)
            .Where(i => i.ArchivedAt == null && i.IsPinned)
            .OrderByDescending(i => i.UpdatedAt)
            .Take(count)
            .ToListAsync();
    }

    public async Task<List<Item>> GetFavoritesAsync(int count = 10)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await WithTags(db.Items)
            .Where(i => i.ArchivedAt == null && i.IsFavorite)
            .OrderByDescending(i => i.UpdatedAt)
            .Take(count)
            .ToListAsync();
    }

    // ---- Reminders / Todos ----

    public async Task<List<Item>> GetUpcomingRemindersAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var now = DateTime.UtcNow;
        return await WithTags(db.Items)
            .Where(i => i.ArchivedAt == null && i.Status != ItemStatus.Done
                        && i.ReminderDate != null && i.ReminderDate >= now)
            .OrderBy(i => i.ReminderDate)
            .ToListAsync();
    }

    public async Task<List<Item>> GetOverdueRemindersAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var now = DateTime.UtcNow;
        return await WithTags(db.Items)
            .Where(i => i.ArchivedAt == null && i.Status != ItemStatus.Done
                        && i.ReminderDate != null && i.ReminderDate < now)
            .OrderBy(i => i.ReminderDate)
            .ToListAsync();
    }

    public async Task<List<Item>> GetTodosAsync(bool includeCompleted = false)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var query = WithTags(db.Items)
            .Where(i => i.ArchivedAt == null && i.ItemType == ItemType.Todo);
        if (!includeCompleted)
            query = query.Where(i => i.Status != ItemStatus.Done);

        return await query
            .OrderByDescending(i => i.IsPinned)
            .ThenBy(i => i.DueDate == null)        // items with a due date first
            .ThenBy(i => i.DueDate)
            .ThenByDescending(i => i.CreatedAt)
            .ToListAsync();
    }
}
