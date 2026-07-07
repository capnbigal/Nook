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
        // "new" is a prefix of "New Note"/"New Project"; a subsequence-only hit must rank lower
        var all = new List<Command> { C("Rename Node"), C("New Note") };
        var hits = CommandRegistry.Match("new", all).Select(c => c.Label).ToList();
        Assert.Equal("New Note", hits[0]); // prefix beats subsequence-in-"reName"
    }

    [Fact]
    public void NoMatch_ReturnsEmpty()
    {
        Assert.Empty(CommandRegistry.Match("zzzz", All));
    }
}
