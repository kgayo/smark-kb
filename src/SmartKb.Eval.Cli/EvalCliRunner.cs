using System.Text.Json;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Eval.Models;

namespace SmartKb.Eval.Cli;

/// <summary>
/// Core CLI logic for running evaluations. Separated from Program for testability.
/// </summary>
public sealed class EvalCliRunner
{
    private readonly EvalSettings _settings;

    public EvalCliRunner(EvalSettings? settings = null)
    {
        _settings = settings ?? new EvalSettings();
    }

    /// <summary>
    /// Runs the full eval pipeline: load dataset, run eval, compare baseline, check thresholds.
    /// Returns exit code: 0 = pass, 1 = violation/blocking regression, 2 = error.
    /// </summary>
    public async Task<EvalCliResult> RunAsync(EvalCliOptions options, CancellationToken cancellationToken = default)
    {
        // Load gold dataset
        var allCases = await GoldDatasetLoader.LoadFromFileAsync(options.DatasetPath, cancellationToken);

        var duplicateIds = GoldDatasetLoader.FindDuplicateIds(allCases);
        if (duplicateIds.Count > 0)
            return EvalCliResult.Error($"Duplicate case IDs: {string.Join(", ", duplicateIds)}");

        // Select cases based on mode
        var cases = options.Mode switch
        {
            EvalMode.Smoke => SelectSmokeCases(allCases, options.SmokeCaseCount),
            EvalMode.Full => allCases,
            _ => allCases,
        };

        // Check minimum case count
        if (!ThresholdChecker.MeetsMinimumCaseCount(cases.Count, _settings))
            return EvalCliResult.Error(
                $"Insufficient cases: {cases.Count} < {_settings.MinCasesForGatedRelease} minimum");

        // Run eval
        EvalReport report;
        if (options.Orchestrator is not null)
        {
            var runner = new EvalRunner(options.Orchestrator, _settings);
            report = await runner.RunAsync(cases, cancellationToken);
        }
        else
        {
            // Offline mode: validate dataset and check baseline only
            return await RunOfflineAsync(cases, options, cancellationToken);
        }

        // Check thresholds
        var violations = ThresholdChecker.Check(report.Metrics, _settings);

        // Compare against baseline if available
        RegressionResult? regression = null;
        if (!string.IsNullOrEmpty(options.BaselinePath))
        {
            var baseline = await BaselineComparator.LoadBaselineAsync(options.BaselinePath, cancellationToken);
            if (baseline is not null)
                regression = BaselineComparator.Compare(report.Metrics, baseline.Metrics, _settings);
        }

        // Save report
        if (!string.IsNullOrEmpty(options.OutputPath))
        {
            var reportJson = GitHubActionsFormatter.SerializeReport(report);
            await File.WriteAllTextAsync(options.OutputPath, reportJson, cancellationToken);
        }

        // Update baseline if requested and no blocking issues
        var shouldBlock = violations.Count > 0 || regression?.ShouldBlock == true;
        if (options.UpdateBaseline && !shouldBlock && !string.IsNullOrEmpty(options.BaselinePath))
        {
            await BaselineComparator.SaveBaselineAsync(report, options.BaselinePath, cancellationToken);
        }

        // Format output
        var annotations = GitHubActionsFormatter.FormatAnnotations(report, violations, regression);
        var summary = GitHubActionsFormatter.FormatJobSummary(report, violations, regression,
            options.Mode == EvalMode.Smoke ? "Nightly Smoke" : "Weekly Full");

        // Send webhook notification if configured (P3-007)
        var notificationSent = await SendNotificationAsync(
            options.NotificationService, report, violations, regression, options.RunUrl, cancellationToken);

        return new EvalCliResult
        {
            ExitCode = shouldBlock ? 1 : 0,
            Report = report,
            Violations = violations,
            Regression = regression,
            Annotations = annotations,
            Summary = summary,
            NotificationSent = notificationSent,
        };
    }

    private async Task<EvalCliResult> RunOfflineAsync(
        IReadOnlyList<EvalCase> cases,
        EvalCliOptions options,
        CancellationToken cancellationToken)
    {
        // In offline mode, we validate the dataset and check the existing baseline thresholds
        var baseline = !string.IsNullOrEmpty(options.BaselinePath)
            ? await BaselineComparator.LoadBaselineAsync(options.BaselinePath, cancellationToken)
            : null;

        if (baseline is null)
        {
            return new EvalCliResult
            {
                ExitCode = 0,
                Annotations = $"::notice title=Eval Offline::Dataset validated: {cases.Count} cases. No baseline to compare.\n",
                Summary = $"# Eval Offline Validation\n\nDataset validated: {cases.Count} cases. No baseline available for threshold checks.\n",
            };
        }

        var violations = ThresholdChecker.Check(baseline.Metrics, _settings);

        return new EvalCliResult
        {
            ExitCode = violations.Count > 0 ? 1 : 0,
            Violations = violations,
            Annotations = $"::notice title=Eval Offline::Dataset validated: {cases.Count} cases. Baseline from {baseline.RunId} checked.\n",
            Summary = $"# Eval Offline Validation\n\nDataset validated: {cases.Count} cases.\nBaseline `{baseline.RunId}` threshold check: {(violations.Count == 0 ? "PASS" : $"{violations.Count} violations")}.\n",
        };
    }

    internal static async Task<bool?> SendNotificationAsync(
        IEvalNotificationService? notificationService,
        EvalReport report,
        IReadOnlyList<ThresholdViolation> violations,
        RegressionResult? regression,
        string? runUrl,
        CancellationToken cancellationToken,
        ILogger? logger = null)
    {
        if (notificationService is null)
            return null; // No notification service configured.

        var hasBlockingRegression = regression?.ShouldBlock == true;
        var payload = new EvalNotificationPayload
        {
            RunId = report.RunId,
            RunType = report.Results.Count > 30 ? "full" : "smoke",
            TotalCases = report.TotalCases,
            SuccessfulCases = report.SuccessfulCases,
            FailedCases = report.FailedCases,
            HasBlockingRegression = hasBlockingRegression,
            ViolationCount = violations.Count,
            Violations = violations.Select(v => new EvalViolationDto
            {
                MetricName = v.MetricName,
                ActualValue = v.ActualValue,
                ThresholdValue = v.ThresholdValue,
                Direction = v.Direction,
            }).ToList(),
            BaselineComparison = regression is not null
                ? new EvalBaselineComparisonDto
                {
                    HasRegression = regression.HasRegression,
                    ShouldBlock = regression.ShouldBlock,
                    Details = regression.Details.Select(d => new EvalRegressionDetailDto
                    {
                        MetricName = d.MetricName,
                        BaselineValue = d.BaselineValue,
                        CurrentValue = d.CurrentValue,
                        Delta = d.Delta,
                        Severity = d.Severity,
                    }).ToList(),
                }
                : null,
            RunUrl = runUrl,
        };

        try
        {
            var success = await notificationService.NotifyAsync(payload, cancellationToken);
            if (success)
                Diagnostics.EvalNotificationsSentTotal.Add(1);
            else
                Diagnostics.EvalNotificationFailuresTotal.Add(1);
            return success;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogWarning(ex, "Eval notification failed for RunId {RunId}", report.RunId);
            Diagnostics.EvalNotificationFailuresTotal.Add(1);
            return false;
        }
    }

    internal static IReadOnlyList<EvalCase> SelectSmokeCases(IReadOnlyList<EvalCase> allCases, int count)
    {
        if (allCases.Count <= count)
            return allCases;

        // Stratified sampling: try to get representative cases across tags
        var tagGroups = allCases
            .SelectMany(c => c.Tags.Select(t => (Tag: t, Case: c)))
            .GroupBy(x => x.Tag)
            .OrderByDescending(g => g.Count())
            .ToList();

        var selected = new HashSet<string>();
        var result = new List<EvalCase>();

        // Round-robin across tag groups until we hit the count
        var groupIndex = 0;
        while (result.Count < count && selected.Count < allCases.Count)
        {
            var group = tagGroups[groupIndex % tagGroups.Count];
            var candidate = group.FirstOrDefault(x => !selected.Contains(x.Case.Id));
            if (candidate.Case is not null && selected.Add(candidate.Case.Id))
            {
                result.Add(candidate.Case);
            }
            groupIndex++;

            // Safety: if we've cycled through all groups without adding, break
            if (groupIndex > tagGroups.Count * 2 && result.Count == selected.Count)
                break;
        }

        // Fill remaining slots if needed
        foreach (var c in allCases)
        {
            if (result.Count >= count) break;
            if (selected.Add(c.Id))
                result.Add(c);
        }

        return result;
    }
}

public sealed record EvalCliOptions
{
    public required string DatasetPath { get; init; }
    public string? BaselinePath { get; init; }
    public string? OutputPath { get; init; }
    public EvalMode Mode { get; init; } = EvalMode.Full;
    public int SmokeCaseCount { get; init; } = 30;
    public bool UpdateBaseline { get; init; }
    public SmartKb.Contracts.Services.IChatOrchestrator? Orchestrator { get; init; }

    /// <summary>Optional notification service for sending regression alerts (P3-007).</summary>
    public IEvalNotificationService? NotificationService { get; init; }

    /// <summary>Optional URL linking to this eval run (e.g., GitHub Actions run URL).</summary>
    public string? RunUrl { get; init; }
}

public enum EvalMode
{
    Smoke,
    Full,
}

public sealed record EvalCliResult
{
    public int ExitCode { get; init; }
    public EvalReport? Report { get; init; }
    public IReadOnlyList<ThresholdViolation>? Violations { get; init; }
    public RegressionResult? Regression { get; init; }
    public string Annotations { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;

    /// <summary>True if notification was sent successfully, false if it failed, null if not configured.</summary>
    public bool? NotificationSent { get; init; }

    public static EvalCliResult Error(string message) => new()
    {
        ExitCode = 2,
        Annotations = $"::error title=Eval Error::{message}\n",
        Summary = $"# Eval Error\n\n{message}\n",
    };
}
