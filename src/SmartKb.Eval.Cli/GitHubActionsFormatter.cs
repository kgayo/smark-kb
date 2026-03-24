using System.Text;
using System.Text.Json;
using SmartKb.Contracts;
using SmartKb.Eval.Models;

namespace SmartKb.Eval.Cli;

/// <summary>
/// Formats eval results as GitHub Actions annotations and job summary markdown.
/// </summary>
public static class GitHubActionsFormatter
{
    /// <summary>
    /// Emits GitHub Actions workflow commands (annotations) for threshold violations and regressions.
    /// </summary>
    public static string FormatAnnotations(
        EvalReport report,
        IReadOnlyList<ThresholdViolation> violations,
        RegressionResult? regression)
    {
        var sb = new StringBuilder();

        // Summary annotation
        sb.AppendLine($"::notice title=Eval Run {report.RunId}::{report.TotalCases} cases evaluated, {report.SuccessfulCases} successful, {report.FailedCases} failed");

        // Threshold violations as warnings or errors
        foreach (var v in violations)
        {
            sb.AppendLine($"::error title=Threshold Violation: {v.MetricName}::{v}");
        }

        // Regression details
        if (regression is not null)
        {
            foreach (var detail in regression.Details)
            {
                if (detail.Severity == "blocking")
                    sb.AppendLine($"::error title=Blocking Regression: {detail.MetricName}::baseline={detail.BaselineValue:F3} current={detail.CurrentValue:F3} delta={detail.Delta:F3}");
                else if (detail.Severity == "warning")
                    sb.AppendLine($"::warning title=Regression Warning: {detail.MetricName}::baseline={detail.BaselineValue:F3} current={detail.CurrentValue:F3} delta={detail.Delta:F3}");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Generates a GitHub Actions job summary in markdown format.
    /// </summary>
    public static string FormatJobSummary(
        EvalReport report,
        IReadOnlyList<ThresholdViolation> violations,
        RegressionResult? regression,
        string mode)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"# Eval Report — {mode}");
        sb.AppendLine();
        sb.AppendLine($"**Run ID:** `{report.RunId}`");
        sb.AppendLine($"**Timestamp:** {report.Timestamp:u}");
        sb.AppendLine($"**Cases:** {report.TotalCases} total, {report.SuccessfulCases} success, {report.FailedCases} failed");
        sb.AppendLine();

        // Metrics table
        sb.AppendLine("## Aggregate Metrics");
        sb.AppendLine();
        sb.AppendLine("| Metric | Value | Threshold | Status |");
        sb.AppendLine("|--------|-------|-----------|--------|");

        var m = report.Metrics;
        var violationMap = violations.ToDictionary(v => v.MetricName);

        AppendMetricRow(sb, "Groundedness", m.Groundedness, violationMap);
        AppendMetricRow(sb, "CitationCoverage", m.CitationCoverage, violationMap);
        AppendMetricRow(sb, "RoutingAccuracy", m.RoutingAccuracy, violationMap);
        AppendMetricRow(sb, "NoEvidenceRate", m.NoEvidenceRate, violationMap);
        AppendMetricRow(sb, "ResponseTypeAccuracy", m.ResponseTypeAccuracy, null);
        AppendMetricRow(sb, "MustIncludeHitRate", m.MustIncludeHitRate, null);
        AppendMetricRow(sb, "SafetyPassRate", m.SafetyPassRate, null);
        AppendMetricRow(sb, "AverageConfidence", m.AverageConfidence, null);

        sb.AppendLine();
        sb.AppendLine($"**Average Duration:** {m.AverageDurationMs}ms");

        // Regression section
        if (regression is not null)
        {
            sb.AppendLine();
            sb.AppendLine("## Baseline Comparison");
            sb.AppendLine();

            if (!regression.HasRegression)
            {
                sb.AppendLine("No regressions detected.");
            }
            else
            {
                sb.AppendLine("| Metric | Baseline | Current | Delta | Severity |");
                sb.AppendLine("|--------|----------|---------|-------|----------|");

                foreach (var d in regression.Details.Where(d => d.Severity != "ok"))
                {
                    var icon = d.Severity == "blocking" ? "BLOCK" : "WARN";
                    sb.AppendLine($"| {d.MetricName} | {d.BaselineValue:F3} | {d.CurrentValue:F3} | {d.Delta:+0.000;-0.000} | {icon} |");
                }
            }
        }

        // Threshold violations summary
        if (violations.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Threshold Violations");
            sb.AppendLine();
            foreach (var v in violations)
                sb.AppendLine($"- **{v.MetricName}**: {v.ActualValue:F3} (threshold {v.Direction} {v.ThresholdValue:F3})");
        }

        // Failed cases
        var failedCases = report.Results.Where(r => r.Error is not null).ToList();
        if (failedCases.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Failed Cases");
            sb.AppendLine();
            foreach (var f in failedCases)
                sb.AppendLine($"- `{f.CaseId}`: {f.Error}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Serializes the full eval report to JSON.
    /// </summary>
    public static string SerializeReport(EvalReport report) =>
        JsonSerializer.Serialize(report, SharedJsonOptions.CamelCaseIndented);

    private static void AppendMetricRow(
        StringBuilder sb,
        string metricName,
        float value,
        Dictionary<string, ThresholdViolation>? violationMap)
    {
        ThresholdViolation? v = null;
        var hasViolation = violationMap?.TryGetValue(metricName, out v) == true;
        var threshold = hasViolation ? $"{v!.Direction} {v.ThresholdValue:F3}" : "—";
        var status = hasViolation ? "FAIL" : "PASS";
        sb.AppendLine($"| {metricName} | {value:F3} | {threshold} | {status} |");
    }
}
