using System.Linq;
using Nook.Services;
using Xunit;

namespace Nook.Tests;

public class UserPreferenceServiceTests
{
    private static UserPreferenceService Svc(GraphHarness h, string user) =>
        new(h.Factory, new FakeCurrentUser(user));

    [Fact]
    public async Task GetOrCreate_is_idempotent_per_user()
    {
        var h = new GraphHarness();
        var a = await Svc(h, "u").GetOrCreateAsync();
        var b = await Svc(h, "u").GetOrCreateAsync();
        Assert.Equal(a.Id, b.Id); // same row, not a duplicate
    }

    [Fact]
    public async Task PushRecent_dedupes_caps_12_and_MRU_orders()
    {
        var h = new GraphHarness();
        var svc = Svc(h, "u");
        for (var i = 1; i <= 14; i++) await svc.PushRecentAsync(i);
        await svc.PushRecentAsync(5); // re-visit -> jumps to front, no dupe

        var recent = await svc.GetRecentIdsAsync();
        Assert.Equal(12, recent.Count);
        Assert.Equal(5, recent[0]);
        Assert.Equal(recent.Distinct().Count(), recent.Count);
        Assert.DoesNotContain(1, recent); // oldest evicted past cap
    }

    [Fact]
    public async Task SetDarkMode_persists()
    {
        var h = new GraphHarness();
        await Svc(h, "u").SetDarkModeAsync(true);
        Assert.True((await Svc(h, "u").GetOrCreateAsync()).IsDarkMode);
    }

    [Fact]
    public async Task Preferences_are_user_isolated()
    {
        var h = new GraphHarness();
        await Svc(h, "a").SetDarkModeAsync(true);
        Assert.False((await Svc(h, "b").GetOrCreateAsync()).IsDarkMode);
    }
}
