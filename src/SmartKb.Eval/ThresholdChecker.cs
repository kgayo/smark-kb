using SmartKb.Contracts;
using SmartKb.Eval.Models;

namespace SmartKb.Eval;

/// <summary>
/// Checks whether aggregate metrics meet configured quality thresholds (P0-021).
/// Used for release gating.
/// </summary>
public static class ThresholdChecker
{
    /// <summary>
    /// Checks aggregate metrics against configured thresholds.
    /// Returns a list of threshold violations.
    /// </summary>
    public static IReadOnlyList<ThresholdViolation> Check(AggregateMetrics metrics, EvalSettings settings)
    {
        var violations = new List<ThresholdViolation>();

        if (metrics.Groundedness < settings.GroundednessThreshold)
        {
            violations.Add(new ThresholdViolation
            {
                MetricName = "Groundedness",
                ActualValue = metrics.Groundedness,
                ThresholdValue = settings.GroundednessThreshold,
                Direction = ThresholdDirection.GreaterThanOrEqual,
            });
        }

        if (metrics.CitationCoverage < settings.CitationCoverageThreshold)
        {
            violations.Add(new ThresholdViolation
            {
                MetricName = "CitationCoverage",
                ActualValue = metrics.CitationCoverage,
                ThresholdValue = settings.CitationCoverageThreshold,
                Direction = ThresholdDirection.GreaterThanOrEqual,
            });
        }

        if (metrics.RoutingAccuracy < settings.RoutingAccuracyThreshold)
        {
            violations.Add(new ThresholdViolation
            {
                MetricName = "RoutingAccuracy",
                ActualValue = metrics.RoutingAccuracy,
                ThresholdValue = settings.RoutingAccuracyThreshold,
                Direction = ThresholdDirection.GreaterThanOrEqual,
            });
        }

        if (metrics.NoEvidenceRate > settings.MaxNoEvidenceRate)
        {
            violations.Add(new ThresholdViolation
            {
                MetricName = "NoEvidenceRate",
                ActualValue = metrics.NoEvidenceRate,
                ThresholdValue = settings.MaxNoEvidenceRate,
                Direction = ThresholdDirection.LessThanOrEqual,
            });
        }

        return violations;
    }

    /// <summary>
    /// Checks whether the gold dataset meets the minimum case count for gated release.
    /// </summary>
    public static bool MeetsMinimumCaseCount(int caseCount, EvalSettings settings) =>
        caseCount >= settings.MinCasesForGatedRelease;
}

/// <summary>
/// A single threshold violation.
/// </summary>
public sealed record ThresholdViolation
{
    public required string MetricName { get; init; }
    public required float ActualValue { get; init; }
    public required float ThresholdValue { get; init; }
    public required string Direction { get; init; }

    public override string ToString() =>
        $"{MetricName}: {ActualValue:F3} (threshold {Direction} {ThresholdValue:F3})";
}
