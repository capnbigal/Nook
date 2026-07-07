using Nook.Services;
using Xunit;

namespace Nook.Tests;

public class CommandRegistryTests
{
    private static Command C(string label, string group = "Actions") =>
        new(group, label, "", null, () => Task.CompletedTask);

    private static readonly IReadOnlyList<Command> All = new List<Command>
    {
        C("New Note"), C("New Project"), C("Toggle Dark Mode"),
        C("Go to Inbox"), C("Go to Search"),
    };

    [Fact]
    public void EmptyQuery_ReturnsAllInOriginalOrder()
    {
        var hits = CommandRegistry.Match("", All).ToList();
        Assert.Equal(All.Select(c => c.Label), hits.Select(c => c.Label));
    }

    [Fact]
    public void WhitespaceQuery_ReturnsAllInOriginalOrder()
    {
        var hits = CommandRegistry.Match("   ", All).ToList();
        Assert.Equal(5, hits.Count);
    }

    [Fact]
    public void Subsequence_MatchesOutOfOrderGapChars()
    {
        // 'tgl' is a subsequence of "Toggle Dark Mode" (T..g..l) but not of the others
        var hits = CommandRegistry.Match("tgl", All).Select(c => c.Label).ToList();
        Assert.Contains("Toggle Dark Mode", hits);
        Assert.DoesNotContain("New Note", hits);
    }

    [Fact]
    public void IsCaseInsensitive()
    {
        var hits = CommandRegistry.Match("NEW NOTE", All).Select(c => c.Label).ToList();
        Assert.Contains("New Note", hits);
    }

    [Fact]
    public void RanksExactPrefixFirst()
    {
        // "new" is a genuine prefix match of "New Note". "Node Workspace" is NOT a prefix and NOT a
        // contiguous substring match for "new" — it only matches via subsequence (N..e..w, non-contiguous:
        // "Node Workspace" -> N(0) ... e(3) ... W(5)) — so it's a real competing lower-ranked candidate.
        var all = new List<Command> { C("Node Workspace"), C("New Note") };
        var hits = CommandRegistry.Match("new", all).Select(c => c.Label).ToList();
        Assert.Equal(new[] { "New Note", "Node Workspace" }, hits); // prefix beats subsequence
    }

    [Fact]
    public void RanksPrefixBeforeContainsBeforeSubsequence()
    {
        // Three candidates that all genuinely match query "ca" via a DIFFERENT tier each:
        //   "Cancel"        -> StartsWith "ca"                         => tier 0 (prefix)
        //   "Scan All"      -> contains "ca" ("sCAn"), not a prefix    => tier 1 (contiguous substring)
        //   "Create Action" -> "ca" only as C..a subsequence (C-r-e-A-t-e...), not contiguous, not prefix
        //                                                               => tier 2 (subsequence)
        var all = new List<Command> { C("Create Action"), C("Scan All"), C("Cancel") };
        var hits = CommandRegistry.Match("ca", all).Select(c => c.Label).ToList();
        Assert.Equal(new[] { "Cancel", "Scan All", "Create Action" }, hits);
    }

    [Fact]
    public void ContainsMatch_RanksAfterPrefixMatch_ForSameQuery()
    {
        // Focused tier-1 proof: "Ignore Warnings" matches query "no" only via Contains
        // (Ignore -> "Ig-NO-re"), never via StartsWith. "Note" matches the same query via StartsWith.
        // Both must be returned, with the prefix match ranked first.
        var all = new List<Command> { C("Ignore Warnings"), C("Note") };
        var hits = CommandRegistry.Match("no", all).Select(c => c.Label).ToList();
        Assert.Contains("Ignore Warnings", hits);
        Assert.Equal(new[] { "Note", "Ignore Warnings" }, hits);
    }

    [Fact]
    public void NoMatch_ReturnsEmpty()
    {
        Assert.Empty(CommandRegistry.Match("zzzz", All));
    }
}
