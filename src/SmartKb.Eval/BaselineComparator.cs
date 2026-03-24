using System.Text.Json;
using SmartKb.Contracts;
using SmartKb.Eval.Models;

namespace SmartKb.Eval;

/// <summary>
/// Compares current eval metrics against a stored baseline to detect regressions.
/// Flag on >2% regression (warning), block on >5% regression (P0-021).
/// </summary>
public static class BaselineComparator
{
    /// <summary>
    /// Compares current metrics against a baseline. Returns regression details for each tracked metric.
    /// Higher-is-better metrics: groundedness, citation coverage, routing accuracy, response type accuracy, safety pass rate.
    /// Lower-is-better metrics: no-evidence rate.
    /// </summary>
    public static RegressionResult Compare(
        AggregateMetrics current,
        AggregateMetrics baseline,
        EvalSettings settings)
    {
        var details = new List<RegressionDetail>();

        // Higher-is-better metrics: regression = baseline - current > threshold
        AddHigherIsBetter(details, "Groundedness", baseline.Groundedness, current.Groundedness, settings);
        AddHigherIsBetter(details, "CitationCoverage", baseline.CitationCoverage, current.CitationCoverage, settings);
        AddHigherIsBetter(details, "RoutingAccuracy", baseline.RoutingAccuracy, current.RoutingAccuracy, settings);
        AddHigherIsBetter(details, "ResponseTypeAccuracy", baseline.ResponseTypeAccuracy, current.ResponseTypeAccuracy, settings);
        AddHigherIsBetter(details, "SafetyPassRate", baseline.SafetyPassRate, current.SafetyPassRate, settings);
        AddHigherIsBetter(details, "MustIncludeHitRate", baseline.MustIncludeHitRate, current.MustIncludeHitRate, settings);

        // Lower-is-better metrics: regression = current - baseline > threshold
        AddLowerIsBetter(details, "NoEvidenceRate", baseline.NoEvidenceRate, current.NoEvidenceRate, settings);

        var hasRegression = details.Any(d => d.Severity is "warning" or "blocking");
        var shouldBlock = details.Any(d => d.Severity == "blocking");

        return new RegressionResult
        {
            HasRegression = hasRegression,
            ShouldBlock = shouldBlock,
            Details = details,
        };
    }

    /// <summary>
    /// Loads a baseline from a JSON file.
    /// </summary>
    public static async Task<EvalBaseline?> LoadBaselineAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            return null;

        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        return JsonSerializer.Deserialize<EvalBaseline>(json, SharedJsonOptions.CaseInsensitiveIndented);
    }

    /// <summary>
    /// Saves current eval results as the new baseline.
    /// </summary>
    public static async Task SaveBaselineAsync(EvalReport report, string filePath, CancellationToken cancellationToken = default)
    {
        var baseline = new EvalBaseline
        {
            Timestamp = report.Timestamp,
            RunId = report.RunId,
            TotalCases = report.TotalCases,
            Metrics = report.Metrics,
        };

        var json = JsonSerializer.Serialize(baseline, SharedJsonOptions.CaseInsensitiveIndented);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);
    }

    /// <summary>
    /// Deserializes a baseline from a JSON string (for testing).
    /// </summary>
    public static EvalBaseline? DeserializeBaseline(string json)
    {
        return JsonSerializer.Deserialize<EvalBaseline>(json, SharedJsonOptions.CaseInsensitiveIndented);
    }

    /// <summary>
    /// Serializes a baseline to a JSON string (for testing).
    /// </summary>
    public static string SerializeBaseline(EvalBaseline baseline)
    {
        return JsonSerializer.Serialize(baseline, SharedJsonOptions.CaseInsensitiveIndented);
    }

    private static void AddHigherIsBetter(
        List<RegressionDetail> details,
        string metricName,
        float baselineValue,
        float currentValue,
        EvalSettings settings)
    {
        var delta = baselineValue - currentValue; // positive means regression
        var severity = delta >= settings.RegressionBlockingThreshold ? "blocking"
            : delta >= settings.RegressionWarningThreshold ? "warning"
            : "ok";

        details.Add(new RegressionDetail
        {
            MetricName = metricName,
            BaselineValue = baselineValue,
            CurrentValue = currentValue,
            Delta = delta,
            Severity = severity,
        });
    }

    private static void AddLowerIsBetter(
        List<RegressionDetail> details,
        string metricName,
        float baselineValue,
        float currentValue,
        EvalSettings settings)
    {
        var delta = currentValue - baselineValue; // positive means regression (rate went up)
        var severity = delta >= settings.RegressionBlockingThreshold ? "blocking"
            : delta >= settings.RegressionWarningThreshold ? "warning"
            : "ok";

        details.Add(new RegressionDetail
        {
            MetricName = metricName,
            BaselineValue = baselineValue,
            CurrentValue = currentValue,
            Delta = delta,
            Severity = severity,
        });
    }
}
