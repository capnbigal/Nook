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
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        // Only remove a link the user owns (links are created between the user's
        // own items, so source ownership identifies the link as theirs).
        var link = await db.ItemLinks
            .FirstOrDefaultAsync(l => l.ItemLinkId == itemLinkId && l.SourceItem!.UserId == userId);
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
