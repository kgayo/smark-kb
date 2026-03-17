namespace SmartKb.Eval;

/// <summary>
/// Configuration for the evaluation harness. Thresholds per P0-021 proposal.
/// </summary>
public sealed class EvalSettings
{
    /// <summary>Minimum acceptable groundedness score (default 0.80).</summary>
    public float GroundednessThreshold { get; init; } = 0.80f;

    /// <summary>Minimum acceptable citation coverage rate (default 0.70).</summary>
    public float CitationCoverageThreshold { get; init; } = 0.70f;

    /// <summary>Minimum acceptable routing accuracy (default 0.60).</summary>
    public float RoutingAccuracyThreshold { get; init; } = 0.60f;

    /// <summary>Maximum acceptable no-evidence rate (default 0.25).</summary>
    public float MaxNoEvidenceRate { get; init; } = 0.25f;

    /// <summary>Regression delta that triggers a warning (default 0.02 = 2%).</summary>
    public float RegressionWarningThreshold { get; init; } = 0.02f;

    /// <summary>Regression delta that blocks release (default 0.05 = 5%).</summary>
    public float RegressionBlockingThreshold { get; init; } = 0.05f;

    /// <summary>Minimum number of gold dataset cases required before gated release (default 30, per D-007).</summary>
    public int MinCasesForGatedRelease { get; init; } = 30;

    /// <summary>Validates that all thresholds are within valid ranges.</summary>
    public bool IsValid =>
        GroundednessThreshold is >= 0f and <= 1f &&
        CitationCoverageThreshold is >= 0f and <= 1f &&
        RoutingAccuracyThreshold is >= 0f and <= 1f &&
        MaxNoEvidenceRate is >= 0f and <= 1f &&
        RegressionWarningThreshold is >= 0f and <= 1f &&
        RegressionBlockingThreshold is >= 0f and <= 1f &&
        RegressionBlockingThreshold >= RegressionWarningThreshold &&
        MinCasesForGatedRelease > 0;
}
