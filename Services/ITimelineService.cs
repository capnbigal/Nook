namespace Nook.Services;

/// <summary>Builds the timeline (day groups interleaved with shoutout cards) for a user.</summary>
public interface ITimelineService
{
    Task<List<TimelineEntry>> BuildAsync(string userId);
}
