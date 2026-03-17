namespace SmartKb.Eval.Models;

/// <summary>
/// Aggregate evaluation report from a full harness run.
/// </summary>
public sealed record EvalReport
{
    public required DateTimeOffset Timestamp { get; init; }
    public required string RunId { get; init; }
    public required int TotalCases { get; init; }
    public required int SuccessfulCases { get; init; }
    public required int FailedCases { get; init; }
    public required AggregateMetrics Metrics { get; init; }
    public required IReadOnlyList<EvalResult> Results { get; init; }
}

/// <summary>
/// Aggregate metrics across all evaluated cases. Thresholds per IMPLEMENTATION_PLAN P0-021:
/// groundedness >= 0.80, citation coverage >= 0.70, routing accuracy >= 0.60, no-evidence rate &lt;= 0.25.
/// </summary>
public sealed record AggregateMetrics
{
    /// <summary>Average groundedness across cases with must_include expectations.</summary>
    public float Groundedness { get; init; }

    /// <summary>Proportion of cases where citation expectations were met.</summary>
    public float CitationCoverage { get; init; }

    /// <summary>Proportion of cases where escalation routing was correct.</summary>
    public float RoutingAccuracy { get; init; }

    /// <summary>Proportion of cases where HasEvidence=false (should be low).</summary>
    public float NoEvidenceRate { get; init; }

    /// <summary>Proportion of cases where response type matched expected.</summary>
    public float ResponseTypeAccuracy { get; init; }

    /// <summary>Average must-include keyword hit rate.</summary>
    public float MustIncludeHitRate { get; init; }

    /// <summary>Proportion of cases passing all safety checks (must_not_include).</summary>
    public float SafetyPassRate { get; init; }

    /// <summary>Average blended confidence score.</summary>
    public float AverageConfidence { get; init; }

    /// <summary>Average orchestration duration in milliseconds.</summary>
    public long AverageDurationMs { get; init; }
}
