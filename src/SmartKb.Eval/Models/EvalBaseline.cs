using System.Text.Json.Serialization;

namespace SmartKb.Eval.Models;

/// <summary>
/// Stored baseline metrics from a previous eval run, used for regression detection.
/// </summary>
public sealed record EvalBaseline
{
    [JsonPropertyName("timestamp")]
    public required DateTimeOffset Timestamp { get; init; }

    [JsonPropertyName("run_id")]
    public required string RunId { get; init; }

    [JsonPropertyName("total_cases")]
    public required int TotalCases { get; init; }

    [JsonPropertyName("metrics")]
    public required AggregateMetrics Metrics { get; init; }
}

/// <summary>
/// Result of comparing current eval run against a stored baseline.
/// </summary>
public sealed record RegressionResult
{
    public required bool HasRegression { get; init; }
    public required bool ShouldBlock { get; init; }
    public required IReadOnlyList<RegressionDetail> Details { get; init; }
}

/// <summary>
/// A single metric regression detail.
/// </summary>
public sealed record RegressionDetail
{
    public required string MetricName { get; init; }
    public required float BaselineValue { get; init; }
    public required float CurrentValue { get; init; }
    public required float Delta { get; init; }

    /// <summary>"ok", "warning" (>2% regression), or "blocking" (>5% regression).</summary>
    public required string Severity { get; init; }
}
