using System;
using Nook.Models;
using Nook.Services;
using Xunit;

namespace Nook.Tests;

public class NodeUiTests
{
    [Theory]
    [InlineData(NodeKind.Note, "#4C7DF0", "--kind-note")]
    [InlineData(NodeKind.Project, "#7C5CFF", "--kind-project")]
    [InlineData(NodeKind.Unclassified, "#8C8578", "--kind-unclassified")]
    [InlineData(NodeKind.Event, "#EE5D9A", "--kind-event")]
    public void KindAccent_matches_spec(NodeKind kind, string hex, string var)
    {
        Assert.Equal(hex, NodeUi.KindAccent(kind));
        Assert.Equal(var, NodeUi.KindAccentVar(kind));
    }

    [Fact]
    public void Every_kind_has_a_valid_hex_and_var()
    {
        foreach (NodeKind kind in Enum.GetValues<NodeKind>())
        {
            var hex = NodeUi.KindAccent(kind);
            Assert.Matches("^#[0-9A-Fa-f]{6}$", hex);
            Assert.StartsWith("--kind-", NodeUi.KindAccentVar(kind));
        }
    }
}
