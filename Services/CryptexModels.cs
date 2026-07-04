using Nook.Models;

namespace Nook.Services;

/// <summary>The five facet wheels of the Nookryptex.</summary>
public enum CryptexRing { Kind, Tag, Collection, State, People }

/// <summary>
/// A compact, DB-free projection of one node's facet values, used to drive the
/// cryptex entirely in memory. People are the titles of Person-kind nodes this
/// node is related to.
/// </summary>
public sealed record CryptexNode(
    int NodeId,
    string Title,
    NodeKind Kind,
    NodeState State,
    string? BodyPreview,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Collections,
    IReadOnlyList<string> People);
