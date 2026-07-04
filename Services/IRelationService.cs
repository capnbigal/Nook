using Nook.Models;

namespace Nook.Services;

/// <summary>Result of attempting to add a relation.</summary>
public enum AddRelationResult { Added, Duplicate, SelfLink, InvalidNodes, InvalidType }

/// <summary>Application service for typed relations between nodes.</summary>
public interface IRelationService
{
    Task<List<RelationType>> GetRelationTypesAsync();
    Task<AddRelationResult> AddRelationAsync(int sourceNodeId, int targetNodeId, int relationTypeId, string? note = null);
    Task RemoveRelationAsync(int nodeRelationId);
    Task<NodeConnections> GetConnectionsAsync(int nodeId);
}
