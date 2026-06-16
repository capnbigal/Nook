using Nook.Models;

namespace Nook.Services;

/// <summary>Application service for creating and querying <see cref="Item"/>s.</summary>
public interface IItemService
{
    // ---- Querying ----
    Task<List<Item>> GetItemsAsync(ItemFilter filter);
    Task<Item?> GetByIdAsync(int id);

    // ---- Mutations ----
    Task<Item> CreateAsync(Item item, IEnumerable<int>? tagIds = null);
    Task UpdateAsync(Item item, IEnumerable<int>? tagIds = null);
    Task DeleteAsync(int id);
    Task ArchiveAsync(int id);
    Task UnarchiveAsync(int id);
    Task TogglePinAsync(int id);
    Task ToggleFavoriteAsync(int id);
    Task CompleteAsync(int id);
    Task ReopenAsync(int id);

    // ---- Related items ----
    Task<List<Item>> GetRelatedByTagsAsync(int id, int max = 10);
    Task<List<Item>> GetChildrenAsync(int id);
    Task LinkAsync(int sourceId, int targetId, string? linkType = null);
    Task UnlinkAsync(int itemLinkId);

    // ---- Dashboard ----
    Task<List<Item>> GetRecentlyCreatedAsync(int count = 5);
    Task<List<Item>> GetRecentlyUpdatedAsync(int count = 5);
    Task<List<Item>> GetDueSoonAsync(int days = 7, int count = 10);
    Task<List<Item>> GetOverdueAsync(int count = 10);
    Task<List<Item>> GetPinnedAsync(int count = 10);
    Task<List<Item>> GetFavoritesAsync(int count = 10);

    // ---- Reminders / Todos ----
    Task<List<Item>> GetUpcomingRemindersAsync();
    Task<List<Item>> GetOverdueRemindersAsync();
    Task<List<Item>> GetTodosAsync(bool includeCompleted = false);
}
