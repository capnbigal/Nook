using Nook.Models;

namespace Nook.Services;

/// <summary>Name + usage count, used by the dashboard and tags pages.</summary>
public record TagSummary(int TagId, string Name, string? Color, int ItemCount);

/// <summary>Application service for managing <see cref="Tag"/>s and item assignments.</summary>
public interface ITagService
{
    Task<List<Tag>> GetAllAsync();
    Task<Tag?> GetByIdAsync(int id);

    /// <summary>Creates a tag. Throws if the name already exists.</summary>
    Task<Tag> CreateAsync(string name, string? color = null);

    /// <summary>Returns the existing tag with this name (case-insensitive) or creates it.</summary>
    Task<Tag> GetOrCreateAsync(string name, string? color = null);

    Task UpdateAsync(Tag tag);
    Task DeleteAsync(int id);

    /// <summary>Tag list with node usage counts (the graph model).</summary>
    Task<List<TagSummary>> GetTagSummaryAsync();

    // ---- Node assignments (the graph model) ----
    Task AssignToNodeAsync(int nodeId, int tagId);
    Task RemoveFromNodeAsync(int nodeId, int tagId);

    // ---- Legacy Item assignments (retained until legacy retirement) ----
    Task AssignTagAsync(int itemId, int tagId);
    Task RemoveTagAsync(int itemId, int tagId);
}
