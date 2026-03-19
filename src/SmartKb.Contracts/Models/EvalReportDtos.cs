namespace SmartKb.Contracts.Models;

/// <summary>Summary DTO for eval report list view (P3-021).</summary>
public sealed record EvalReportSummary
{
    public required Guid Id { get; init; }
    public required string RunId { get; init; }
    public required string RunType { get; init; }
    public required int TotalCases { get; init; }
    public required int SuccessfulCases { get; init; }
    public required int FailedCases { get; init; }
    public required bool HasBlockingRegression { get; init; }
    public required int ViolationCount { get; init; }
    public required string TriggeredBy { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Full eval report detail (P3-021).</summary>
public sealed record EvalReportDetail
{
    public required Guid Id { get; init; }
    public required string RunId { get; init; }
    public required string RunType { get; init; }
    public required int TotalCases { get; init; }
    public required int SuccessfulCases { get; init; }
    public required int FailedCases { get; init; }
    public required bool HasBlockingRegression { get; init; }
    public required int ViolationCount { get; init; }
    public required string TriggeredBy { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Deserialized aggregate metrics.</summary>
    public required EvalMetricsDto Metrics { get; init; }

    /// <summary>Threshold violations (empty if none).</summary>
    public required IReadOnlyList<EvalViolationDto> Violations { get; init; }

    /// <summary>Baseline comparison result (null if no baseline was available).</summary>
    public EvalBaselineComparisonDto? BaselineComparison { get; init; }
}

/// <summary>Aggregate metrics from an eval run.</summary>
public sealed record EvalMetricsDto
{
    public float Groundedness { get; init; }
    public float CitationCoverage { get; init; }
    public float RoutingAccuracy { get; init; }
    public float NoEvidenceRate { get; init; }
    public float ResponseTypeAccuracy { get; init; }
    public float MustIncludeHitRate { get; init; }
    public float SafetyPassRate { get; init; }
    public float AverageConfidence { get; init; }
    public long AverageDurationMs { get; init; }
}

/// <summary>A single threshold violation.</summary>
public sealed record EvalViolationDto
{
    public required string MetricName { get; init; }
    public required float ActualValue { get; init; }
    public required float ThresholdValue { get; init; }
    public required string Direction { get; init; }
}

/// <summary>Baseline regression comparison result.</summary>
public sealed record EvalBaselineComparisonDto
{
    public required bool HasRegression { get; init; }
    public required bool ShouldBlock { get; init; }
    public required IReadOnlyList<EvalRegressionDetailDto> Details { get; init; }
}

/// <summary>A single metric regression detail.</summary>
public sealed record EvalRegressionDetailDto
{
    public required string MetricName { get; init; }
    public required float BaselineValue { get; init; }
    public required float CurrentValue { get; init; }
    public required float Delta { get; init; }
    public required string Severity { get; init; }
}

/// <summary>Paginated eval report list response.</summary>
public sealed record EvalReportListResponse
{
    public required IReadOnlyList<EvalReportSummary> Reports { get; init; }
    public required int TotalCount { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required bool HasMore { get; init; }
}

/// <summary>Request to persist an eval report.</summary>
public sealed record PersistEvalReportRequest
{
    public required string RunId { get; init; }
    public required string RunType { get; init; }
    public required int TotalCases { get; init; }
    public required int SuccessfulCases { get; init; }
    public required int FailedCases { get; init; }
    public required string MetricsJson { get; init; }
    public string? ViolationsJson { get; init; }
    public string? BaselineComparisonJson { get; init; }
    public bool HasBlockingRegression { get; init; }
    public int ViolationCount { get; init; }
}
