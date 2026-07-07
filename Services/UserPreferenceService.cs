using Microsoft.EntityFrameworkCore;
using Nook.Data;
using Nook.Models;

namespace Nook.Services;

public sealed class UserPreferenceService : IUserPreferenceService
{
    private const int RecentCap = 12;
    private readonly IDbContextFactory<NookContext> _factory;
    private readonly ICurrentUser _currentUser;

    public UserPreferenceService(IDbContextFactory<NookContext> factory, ICurrentUser currentUser)
    {
        _factory = factory;
        _currentUser = currentUser;
    }

    private static async Task<UserPreference> LoadOrCreateAsync(NookContext db, string userId, CancellationToken ct)
    {
        var pref = await db.UserPreferences.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (pref is null)
        {
            pref = new UserPreference { UserId = userId, RecentNodeIdsCsv = "", UpdatedAt = DateTime.UtcNow };
            db.UserPreferences.Add(pref);
            await db.SaveChangesAsync(ct);
        }
        return pref;
    }

    public async Task<UserPreference> GetOrCreateAsync(CancellationToken ct = default)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await LoadOrCreateAsync(db, userId, ct);
    }

    public Task SetDarkModeAsync(bool on) => MutateAsync(p => p.IsDarkMode = on);
    public Task SetSidebarCollapsedAsync(bool collapsed) => MutateAsync(p => p.SidebarCollapsed = collapsed);
    public Task SetLastOpenedAsync(int nodeId) => MutateAsync(p => p.LastOpenedNodeId = nodeId);

    public Task PushRecentAsync(int nodeId) => MutateAsync(p =>
    {
        var ids = Parse(p.RecentNodeIdsCsv);
        ids.RemoveAll(x => x == nodeId);
        ids.Insert(0, nodeId);
        if (ids.Count > RecentCap) ids = ids.GetRange(0, RecentCap);
        p.RecentNodeIdsCsv = string.Join(',', ids);
    });

    public async Task<IReadOnlyList<int>> GetRecentIdsAsync(CancellationToken ct = default)
    {
        var pref = await GetOrCreateAsync(ct);
        return Parse(pref.RecentNodeIdsCsv);
    }

    private async Task MutateAsync(Action<UserPreference> mutate)
    {
        var userId = await _currentUser.GetRequiredUserIdAsync();
        await using var db = await _factory.CreateDbContextAsync();
        var pref = await LoadOrCreateAsync(db, userId, default);
        mutate(pref);
        pref.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    private static List<int> Parse(string csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? new List<int>()
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries)
                 .Select(s => int.TryParse(s, out var v) ? v : 0)
                 .Where(v => v > 0).ToList();
}
