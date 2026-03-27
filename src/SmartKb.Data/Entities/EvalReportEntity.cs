namespace SmartKb.Data.Entities;

/// <summary>
/// Persisted evaluation report from a harness run (P3-021).
/// Stores aggregate metrics, threshold violations, and baseline comparison results.
/// </summary>
public sealed class EvalReportEntity
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Unique run identifier, e.g. "eval-run-20260319-143000".</summary>
    public string RunId { get; set; } = string.Empty;

    /// <summary>Run type: "smoke" or "full".</summary>
    public string RunType { get; set; } = string.Empty;

    public int TotalCases { get; set; }
    public int SuccessfulCases { get; set; }
    public int FailedCases { get; set; }

    /// <summary>Serialized AggregateMetrics JSON.</summary>
    public string MetricsJson { get; set; } = string.Empty;

    /// <summary>Serialized ThresholdViolation[] JSON (nullable if no violations).</summary>
    public string? ViolationsJson { get; set; }

    /// <summary>Serialized RegressionResult JSON (nullable if no baseline comparison).</summary>
    public string? BaselineComparisonJson { get; set; }

    /// <summary>Whether any blocking regression was detected.</summary>
    public bool HasBlockingRegression { get; set; }

    /// <summary>Number of threshold violations.</summary>
    public int ViolationCount { get; set; }

    /// <summary>Actor who triggered the eval run.</summary>
    public string TriggeredBy { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Unix epoch seconds of <see cref="CreatedAt"/>. Enables server-side filtering in SQLite (which cannot compare DateTimeOffset).</summary>
    public long CreatedAtEpoch { get; set; }

    public TenantEntity Tenant { get; set; } = null!;
}
