using Nook.Models;

namespace Nook.Services;

public interface IUserPreferenceService
{
    Task<UserPreference> GetOrCreateAsync(CancellationToken ct = default);
    Task SetDarkModeAsync(bool on);
    Task SetSidebarCollapsedAsync(bool collapsed);
    Task PushRecentAsync(int nodeId);
    Task SetLastOpenedAsync(int nodeId);
    Task<IReadOnlyList<int>> GetRecentIdsAsync(CancellationToken ct = default);
}
