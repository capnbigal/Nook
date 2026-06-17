using Nook.Models;

namespace Nook.Services;

public sealed record CountSlice(string Label, int Count);

public sealed record WeekPoint(DateOnly WeekStart, int Created, int Completed);

public sealed record AnalyticsModel(
    int TotalItems,
    int OpenItems,
    int CompletedItems,
    double CompletionRatePercent,
    int OverdueCount,
    int UntaggedCount,
    DayOfWeek? BusiestDay,
    IReadOnlyList<CountSlice> ByType,
    IReadOnlyList<CountSlice> ByStatus,
    IReadOnlyList<CountSlice> ByPriority,
    IReadOnlyList<CountSlice> TopTags,
    IReadOnlyList<WeekPoint> WeeklyTrend);
