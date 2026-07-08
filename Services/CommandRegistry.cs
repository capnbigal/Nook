namespace Nook.Services;

public record Command(string Group, string Label, string Icon, string? Shortcut, Func<Task> Invoke);

public static class CommandRegistry
{
    // PURE. Case-insensitive subsequence fuzzy match over Label.
    // Ranking: exact-prefix < contiguous-substring < subsequence; tie-break shorter Label, then original order.
    // Empty/whitespace query => returns `all` in original order (caller supplies recents/defaults ordering).
    public static IEnumerable<Command> Match(string query, IReadOnlyList<Command> all)
    {
        if (string.IsNullOrWhiteSpace(query))
            return all;
        var q = query.Trim();
        return all
            .Select((c, i) => (c, i, rank: Rank(q, c.Label)))
            .Where(x => x.rank >= 0)
            .OrderBy(x => x.rank)
            .ThenBy(x => x.c.Label.Length)
            .ThenBy(x => x.i)
            .Select(x => x.c)
            .ToList();
    }

    // -1 = no match; lower is better (0 prefix, 1 substring, 2 subsequence).
    private static int Rank(string q, string label)
    {
        var l = label ?? string.Empty;
        if (l.StartsWith(q, StringComparison.OrdinalIgnoreCase)) return 0;
        if (l.Contains(q, StringComparison.OrdinalIgnoreCase)) return 1;
        return IsSubsequence(q, l) ? 2 : -1;
    }

    private static bool IsSubsequence(string q, string l)
    {
        int qi = 0;
        for (int li = 0; li < l.Length && qi < q.Length; li++)
            if (char.ToLowerInvariant(l[li]) == char.ToLowerInvariant(q[qi])) qi++;
        return qi == q.Length;
    }
}
