namespace Nook.Services;

/// <summary>
/// Pure, DB-free filtering for the cryptex. A selection maps each *set* ring to a
/// single chosen value; a node matches when it carries every selected value.
/// Per-value counts ignore their own ring so a wheel keeps showing its
/// alternatives while narrowing the others.
/// </summary>
public static class CryptexEngine
{
    public static IEnumerable<string> Values(CryptexNode n, CryptexRing r) => r switch
    {
        CryptexRing.Kind => new[] { n.Kind.ToString() },
        CryptexRing.State => new[] { n.State.ToString() },
        CryptexRing.Tag => n.Tags,
        CryptexRing.Collection => n.Collections,
        CryptexRing.People => n.People,
        _ => System.Array.Empty<string>(),
    };

    public static bool Matches(CryptexNode n, IReadOnlyDictionary<CryptexRing, string> sel, CryptexRing? ignore = null)
        => sel.All(kv => kv.Key == ignore || Values(n, kv.Key).Contains(kv.Value));

    public static List<string> DistinctValues(IEnumerable<CryptexNode> nodes, CryptexRing r)
        => nodes.SelectMany(n => Values(n, r)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToList();

    public static int Count(IEnumerable<CryptexNode> nodes, IReadOnlyDictionary<CryptexRing, string> sel, CryptexRing r, string value)
        => nodes.Count(n => Matches(n, sel, r) && Values(n, r).Contains(value));

    public static List<CryptexNode> Hits(IEnumerable<CryptexNode> nodes, IReadOnlyDictionary<CryptexRing, string> sel)
        => nodes.Where(n => Matches(n, sel)).ToList();
}
