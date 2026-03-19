namespace SmartKb.Contracts.Models;

/// <summary>
/// Usage metrics for a single pattern, computed on-demand from answer traces.
/// </summary>
public sealed record PatternUsageMetrics
{
    public required string PatternId { get; init; }

    /// <summary>Total number of times this pattern was cited in answers.</summary>
    public required int TotalCitations { get; init; }

    /// <summary>Citations in the last 7 days.</summary>
    public required int CitationsLast7Days { get; init; }

    /// <summary>Citations in the last 30 days.</summary>
    public required int CitationsLast30Days { get; init; }

    /// <summary>Citations in the last 90 days.</summary>
    public required int CitationsLast90Days { get; init; }

    /// <summary>Number of distinct sessions that cited this pattern.</summary>
    public required int UniqueUsers { get; init; }

    /// <summary>Average confidence score of answers that cited this pattern.</summary>
    public required float AverageConfidence { get; init; }

    /// <summary>Most recent citation timestamp, null if never cited.</summary>
    public DateTimeOffset? LastCitedAt { get; init; }

    /// <summary>First citation timestamp, null if never cited.</summary>
    public DateTimeOffset? FirstCitedAt { get; init; }

    /// <summary>Per-day citation counts for the last 30 days.</summary>
    public required IReadOnlyList<PatternUsageDayBucket> DailyBreakdown { get; init; }
}

/// <summary>Citation count for a single day.</summary>
public sealed record PatternUsageDayBucket
{
    public required DateOnly Date { get; init; }
    public required int Citations { get; init; }
}
