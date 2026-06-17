using Nook.Models;

namespace Nook.Services;

/// <summary>An entry rendered on the timeline: either a day of events or a shoutout card.</summary>
public abstract record TimelineEntry;

public sealed record DayEntry(DateOnly Date, IReadOnlyList<ActivityLog> Events) : TimelineEntry;

public sealed record ShoutoutEntry(string Text, string Icon) : TimelineEntry;
