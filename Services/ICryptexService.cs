namespace Nook.Services;

/// <summary>Data + write operations backing the Nookryptex page.</summary>
public interface ICryptexService
{
    /// <summary>A compact facet projection of all the current user's nodes (incl. archived).</summary>
    Task<List<CryptexNode>> GetDatasetAsync();

    /// <summary>
    /// Creates a node stamped with the dialed-in code: Kind/State (defaulting to
    /// Unclassified/Inbox), an optional tag, collection membership, and an
    /// "associated with" relation to a Person. Returns the new NodeId.
    /// </summary>
    Task<int> AddNodeWithCodeAsync(string title, IReadOnlyDictionary<CryptexRing, string> code);
}
