using Nook.Models;

namespace Nook.Services;

/// <summary>A single connection shown on a node's detail page (outgoing or backlink).</summary>
public sealed record Connection(
    int NodeRelationId,
    int OtherNodeId,
    string OtherTitle,
    NodeKind OtherKind,
    string Label,
    bool IsOutgoing,
    RelationCategory Category,
    string? Note);

/// <summary>All connections for a node, split into outgoing and backlinks.</summary>
public sealed record NodeConnections(IReadOnlyList<Connection> Outgoing, IReadOnlyList<Connection> Backlinks)
{
    public bool Any => Outgoing.Count > 0 || Backlinks.Count > 0;
}
