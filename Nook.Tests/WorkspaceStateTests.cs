using Nook.Services;
using Xunit;

namespace Nook.Tests;

public class WorkspaceStateTests
{
    [Fact]
    public void MergeRecentIds_MovesVisitedToFront_AndDedupes()
    {
        var result = WorkspaceState.MergeRecentIds(new[] { 3, 1, 2 }, 2);
        Assert.Equal(new[] { 2, 3, 1 }, result);
    }

    [Fact]
    public void MergeRecentIds_CapsAtTwelve()
    {
        var current = Enumerable.Range(1, 12).ToArray(); // 1..12
        var result = WorkspaceState.MergeRecentIds(current, 99);
        Assert.Equal(12, result.Count);
        Assert.Equal(99, result[0]);
        Assert.DoesNotContain(12, result); // oldest evicted
    }

    [Fact]
    public void MergeRecentIds_NewId_PrependsWithoutDuplicating()
    {
        var result = WorkspaceState.MergeRecentIds(new[] { 5, 6 }, 7);
        Assert.Equal(new[] { 7, 5, 6 }, result);
    }
}
