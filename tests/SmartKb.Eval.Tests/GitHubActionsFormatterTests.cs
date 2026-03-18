using SmartKb.Contracts.Models;
using SmartKb.Eval.Cli;
using SmartKb.Eval.Models;

namespace SmartKb.Eval.Tests;

public class GitHubActionsFormatterTests
{
    private static EvalReport CreateReport(AggregateMetrics? metrics = null) => new()
    {
        Timestamp = DateTimeOffset.UtcNow,
        RunId = "test-run-001",
        TotalCases = 30,
        SuccessfulCases = 28,
        FailedCases = 2,
        Metrics = metrics ?? new AggregateMetrics
        {
            Groundedness = 0.85f,
            CitationCoverage = 0.75f,
            RoutingAccuracy = 0.70f,
            NoEvidenceRate = 0.15f,
            ResponseTypeAccuracy = 0.80f,
            MustIncludeHitRate = 0.90f,
            SafetyPassRate = 1.0f,
            AverageConfidence = 0.82f,
            AverageDurationMs = 1500,
        },
        Results = [],
    };

    [Fact]
    public void FormatAnnotations_NoViolationsOrRegressions_EmitsNotice()
    {
        var report = CreateReport();
        var result = GitHubActionsFormatter.FormatAnnotations(report, [], null);

        Assert.Contains("::notice", result);
        Assert.Contains("30 cases evaluated", result);
        Assert.DoesNotContain("::error", result);
    }

    [Fact]
    public void FormatAnnotations_WithViolations_EmitsErrors()
    {
        var report = CreateReport();
        var violations = new List<ThresholdViolation>
        {
            new() { MetricName = "Groundedness", ActualValue = 0.50f, ThresholdValue = 0.80f, Direction = ">=" },
        };

        var result = GitHubActionsFormatter.FormatAnnotations(report, violations, null);

        Assert.Contains("::error title=Threshold Violation: Groundedness", result);
    }

    [Fact]
    public void FormatAnnotations_WithBlockingRegression_EmitsError()
    {
        var report = CreateReport();
        var regression = new RegressionResult
        {
            HasRegression = true,
            ShouldBlock = true,
            Details = [new RegressionDetail
            {
                MetricName = "Groundedness",
                BaselineValue = 0.90f,
                CurrentValue = 0.80f,
                Delta = 0.10f,
                Severity = "blocking",
            }],
        };

        var result = GitHubActionsFormatter.FormatAnnotations(report, [], regression);

        Assert.Contains("::error title=Blocking Regression: Groundedness", result);
    }

    [Fact]
    public void FormatAnnotations_WithWarningRegression_EmitsWarning()
    {
        var report = CreateReport();
        var regression = new RegressionResult
        {
            HasRegression = true,
            ShouldBlock = false,
            Details = [new RegressionDetail
            {
                MetricName = "CitationCoverage",
                BaselineValue = 0.80f,
                CurrentValue = 0.77f,
                Delta = 0.03f,
                Severity = "warning",
            }],
        };

        var result = GitHubActionsFormatter.FormatAnnotations(report, [], regression);

        Assert.Contains("::warning title=Regression Warning: CitationCoverage", result);
    }

    [Fact]
    public void FormatJobSummary_ContainsMetricsTable()
    {
        var report = CreateReport();
        var result = GitHubActionsFormatter.FormatJobSummary(report, [], null, "Nightly Smoke");

        Assert.Contains("# Eval Report", result);
        Assert.Contains("Nightly Smoke", result);
        Assert.Contains("Groundedness", result);
        Assert.Contains("CitationCoverage", result);
        Assert.Contains("RoutingAccuracy", result);
        Assert.Contains("| Metric | Value | Threshold | Status |", result);
    }

    [Fact]
    public void FormatJobSummary_WithViolations_ShowsViolationSection()
    {
        var report = CreateReport();
        var violations = new List<ThresholdViolation>
        {
            new() { MetricName = "Groundedness", ActualValue = 0.50f, ThresholdValue = 0.80f, Direction = ">=" },
        };

        var result = GitHubActionsFormatter.FormatJobSummary(report, violations, null, "Weekly Full");

        Assert.Contains("## Threshold Violations", result);
        Assert.Contains("Groundedness", result);
    }

    [Fact]
    public void FormatJobSummary_WithRegression_ShowsBaselineComparison()
    {
        var report = CreateReport();
        var regression = new RegressionResult
        {
            HasRegression = true,
            ShouldBlock = true,
            Details = [new RegressionDetail
            {
                MetricName = "Groundedness",
                BaselineValue = 0.90f,
                CurrentValue = 0.80f,
                Delta = 0.10f,
                Severity = "blocking",
            }],
        };

        var result = GitHubActionsFormatter.FormatJobSummary(report, [], regression, "Weekly Full");

        Assert.Contains("## Baseline Comparison", result);
        Assert.Contains("BLOCK", result);
    }

    [Fact]
    public void FormatJobSummary_NoRegression_ShowsNoRegressionsMessage()
    {
        var report = CreateReport();
        var regression = new RegressionResult
        {
            HasRegression = false,
            ShouldBlock = false,
            Details = [],
        };

        var result = GitHubActionsFormatter.FormatJobSummary(report, [], regression, "Nightly Smoke");

        Assert.Contains("No regressions detected", result);
    }

    [Fact]
    public void FormatJobSummary_WithFailedCases_ShowsFailedSection()
    {
        var report = new EvalReport
        {
            Timestamp = DateTimeOffset.UtcNow,
            RunId = "test-run-002",
            TotalCases = 5,
            SuccessfulCases = 4,
            FailedCases = 1,
            Metrics = new AggregateMetrics(),
            Results = [new EvalResult
            {
                CaseId = "eval-00001",
                Response = new ChatResponse
                {
                    ResponseType = "error",
                    Answer = "",
                    Citations = [],
                    Confidence = 0f,
                    ConfidenceLabel = "Low",
                    TraceId = "",
                    HasEvidence = false,
                    SystemPromptVersion = "",
                },
                Metrics = new CaseMetrics(),
                Error = "Connection refused",
            }],
        };

        var result = GitHubActionsFormatter.FormatJobSummary(report, [], null, "Weekly Full");

        Assert.Contains("## Failed Cases", result);
        Assert.Contains("eval-00001", result);
        Assert.Contains("Connection refused", result);
    }

    [Fact]
    public void SerializeReport_ProducesValidJson()
    {
        var report = CreateReport();
        var json = GitHubActionsFormatter.SerializeReport(report);

        Assert.Contains("\"runId\"", json);
        Assert.Contains("\"totalCases\"", json);
        Assert.Contains("\"groundedness\"", json);
    }
}
