using Nook.Models;

namespace Nook.Services;

/// <summary>Canonical system-defined relation types and verbs seeded at launch.</summary>
public static class GraphSeedData
{
    public record RelationTypeSeed(string Name, string? InverseName, bool IsSymmetric, RelationCategory Category);

    public static readonly IReadOnlyList<RelationTypeSeed> RelationTypes = new List<RelationTypeSeed>
    {
        new("related to", "related to", true, RelationCategory.General),
        new("associated with", "associated with", true, RelationCategory.General),
        new("contains", "contained by", false, RelationCategory.Structure),
        new("part of", "has part", false, RelationCategory.Structure),
        new("about", "subject of", false, RelationCategory.Reference),
        new("mentions", "mentioned by", false, RelationCategory.Reference),
        new("works with", "works with", true, RelationCategory.People),
        new("works on", "worked on by", false, RelationCategory.People),
        new("belongs to", "owns", false, RelationCategory.Ownership),
        new("supports", "supported by", false, RelationCategory.Reasoning),
        new("depends on", "required by", false, RelationCategory.Planning),
        new("introduced by", "introduced", false, RelationCategory.People),
        new("located at", "location of", false, RelationCategory.Place),
    };

    /// <summary>The generic default used when a legacy link label cannot be mapped.</summary>
    public const string DefaultRelationTypeName = "related to";

    /// <summary>The containment relation used to migrate legacy ParentItemId hierarchy.</summary>
    public const string ContainsRelationTypeName = "contains";

    public static readonly IReadOnlyList<string> Verbs = new List<string>
    {
        "met", "spoke with", "introduced", "watched", "read", "listened to",
        "visited", "attended", "saw", "tried", "bought", "ate at",
        "did", "learned", "told", "noted",
    };
}
