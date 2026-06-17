namespace Nook.Services;

/// <summary>Computes per-user analytics for the analytics dashboard.</summary>
public interface IAnalyticsService
{
    Task<AnalyticsModel> GetForUserAsync(string userId);
}
