using Nook.Models;

namespace Nook.Services;

/// <summary>A collection with its backing node and member count, for list views.</summary>
public sealed record CollectionSummary(Node Node, CollectionKind Kind, bool IsOrdered, int MemberCount);

/// <summary>Application service for node-backed collections and their membership.</summary>
public interface ICollectionService
{
    Task<Node> CreateAsync(string title, CollectionKind kind, string? body = null, string? color = null);

    /// <summary>
    /// Creates a collection and adds <paramref name="memberNodeId"/> to it in a single
    /// save (one transaction). Both the new collection and the member belong to the
    /// current user. Throws if a same-named active collection already exists.
    /// </summary>
    Task<Node> CreateAndAddMemberAsync(string title, CollectionKind kind, string? body, int memberNodeId);

    /// <summary>True when the current user already has an active collection with this name.</summary>
    Task<bool> NameExistsAsync(string name);

    Task UpdateAsync(int collectionNodeId, string title, string? body, CollectionKind kind, string? color);
    Task<List<CollectionSummary>> GetCollectionsAsync(bool includeArchived = false);
    Task<Collection?> GetAsync(int collectionNodeId);
    Task<List<Node>> GetMembersAsync(int collectionNodeId);
    Task<bool> AddMemberAsync(int collectionNodeId, int memberNodeId);
    Task RemoveMemberAsync(int collectionNodeId, int memberNodeId);
    Task MoveMemberAsync(int collectionNodeId, int memberNodeId, bool up);
    Task<List<Node>> GetCollectionsForNodeAsync(int memberNodeId);

    /// <summary>The collections a node belongs to, with type/ordering info for display.</summary>
    Task<List<CollectionSummary>> GetCollectionSummariesForNodeAsync(int memberNodeId);
}
