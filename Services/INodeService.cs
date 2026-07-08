using Nook.Models;

namespace Nook.Services;

/// <summary>Application service for creating, querying and organising nodes.</summary>
public interface INodeService
{
    // ---- Querying ----
    Task<List<Node>> QueryAsync(NodeFilter filter);
    Task<Node?> GetByIdAsync(int id);
    /// <summary>Find the current user's node with an exact (case-sensitive) Title match. Most-recently-updated wins if duplicates exist; null if none.</summary>
    Task<Node?> FindByExactTitleAsync(string title, CancellationToken ct = default);

    // ---- Mutations ----
    Task<Node> CreateAsync(Node node, IEnumerable<int>? tagIds = null);
    Task<Node> QuickCaptureAsync(string title, string? body = null);
    Task UpdateAsync(Node node, IEnumerable<int>? tagIds = null);
    /// <summary>Persist only the Body (and UpdatedAt) of an owned node. Never touches tags/relations/kind/state.</summary>
    Task SaveBodyAsync(int nodeId, string? body, CancellationToken ct = default);
    Task PromoteAsync(int id, NodeKind kind);
    Task SetStateAsync(int id, NodeState state);
    Task ArchiveAsync(int id);
    Task RestoreAsync(int id);
    Task TogglePinAsync(int id);
    Task ToggleFavoriteAsync(int id);
    Task DeleteAsync(int id);

    // ---- Related / dashboards ----
    Task<List<Node>> GetRelatedByTagsAsync(int id, int max = 8);
    Task<List<Node>> GetInboxAsync(int count = 50);
    Task<List<Node>> GetRecentlyUpdatedAsync(int count = 8);
    Task<List<Node>> GetPinnedAsync(int count = 10);
    Task<List<Node>> GetFavoritesAsync(int count = 10);
    Task<int> CountByStateAsync(NodeState state);
}
