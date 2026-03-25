using System.Text.Json;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;

namespace SmartKb.Contracts.Tests;

public class EvalPayloadBuilderTests
{
    private static EvalNotificationPayload CreatePayload(bool blocking = false, int violations = 0) => new()
    {
        RunId = "run-001",
        RunType = "nightly",
        TotalCases = 10,
        SuccessfulCases = 8,
        FailedCases = 2,
        HasBlockingRegression = blocking,
        ViolationCount = violations,
        Violations = violations > 0
            ? [new ThresholdViolation { MetricName = "accuracy", ActualValue = 0.5, ThresholdValue = 0.7, Direction = "gte" }]
            : [],
        BaselineComparison = blocking
            ? new BaselineComparison
            {
                HasRegression = true,
                Details = [new RegressionDetail { MetricName = "latency", BaselineValue = 1.0, CurrentValue = 2.0, Delta = 1.0, Severity = "critical" }],
            }
            : null,
        RunUrl = "https://example.com/runs/001",
    };

    [Fact]
    public void BuildPayload_DefaultFormat_ReturnsGenericJson()
    {
        var payload = CreatePayload();
        var result = EvalPayloadBuilder.BuildPayload("generic", payload);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("eval.regression_alert", doc.RootElement.GetProperty("eventType").GetString());
        Assert.Equal("run-001", doc.RootElement.GetProperty("runId").GetString());
    }

    [Fact]
    public void BuildPayload_SlackFormat_ReturnsSlackJson()
    {
        var payload = CreatePayload(violations: 1);
        var result = EvalPayloadBuilder.BuildPayload("slack", payload);

        using var doc = JsonDocument.Parse(result);
        var text = doc.RootElement.GetProperty("text").GetString()!;
        Assert.Contains(":warning:", text);
        Assert.Contains("Threshold Violations", text);
        Assert.Contains("accuracy", text);
    }

    [Fact]
    public void BuildPayload_SlackFormat_BlockingRegression_UsesRotatingLight()
    {
        var payload = CreatePayload(blocking: true);
        var result = EvalPayloadBuilder.BuildPayload("SLACK", payload);

        using var doc = JsonDocument.Parse(result);
        var text = doc.RootElement.GetProperty("text").GetString()!;
        Assert.Contains(":rotating_light:", text);
        Assert.Contains("BLOCKING REGRESSION", text);
    }

    [Fact]
    public void BuildPayload_TeamsFormat_ReturnsMessageCard()
    {
        var payload = CreatePayload(violations: 1);
        var result = EvalPayloadBuilder.BuildPayload("teams", payload);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("MessageCard", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal("FFA500", doc.RootElement.GetProperty("themeColor").GetString());
    }

    [Fact]
    public void BuildPayload_TeamsFormat_BlockingRegression_RedTheme()
    {
        var payload = CreatePayload(blocking: true);
        var result = EvalPayloadBuilder.BuildPayload("Teams", payload);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("FF0000", doc.RootElement.GetProperty("themeColor").GetString());
    }

    [Fact]
    public void BuildPayload_UnknownFormat_FallsBackToGeneric()
    {
        var payload = CreatePayload();
        var result = EvalPayloadBuilder.BuildPayload("xml", payload);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("eval.regression_alert", doc.RootElement.GetProperty("eventType").GetString());
    }

    [Fact]
    public void BuildGenericPayload_IncludesAllFields()
    {
        var payload = CreatePayload(violations: 1);
        var result = EvalPayloadBuilder.BuildGenericPayload(payload);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("run-001", doc.RootElement.GetProperty("runId").GetString());
        Assert.Equal("nightly", doc.RootElement.GetProperty("runType").GetString());
        Assert.Equal(10, doc.RootElement.GetProperty("totalCases").GetInt32());
        Assert.Equal(8, doc.RootElement.GetProperty("successfulCases").GetInt32());
        Assert.Equal(2, doc.RootElement.GetProperty("failedCases").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("violationCount").GetInt32());
        Assert.Equal("https://example.com/runs/001", doc.RootElement.GetProperty("runUrl").GetString());
    }

    [Fact]
    public void BuildSlackPayload_IncludesRunUrl()
    {
        var payload = CreatePayload();
        var result = EvalPayloadBuilder.BuildSlackPayload(payload);

        using var doc = JsonDocument.Parse(result);
        var text = doc.RootElement.GetProperty("text").GetString()!;
        Assert.Contains("<https://example.com/runs/001|View Run>", text);
    }

    [Fact]
    public void BuildTeamsPayload_IncludesOpenUriAction()
    {
        var payload = CreatePayload();
        var result = EvalPayloadBuilder.BuildTeamsPayload(payload);

        using var doc = JsonDocument.Parse(result);
        var actions = doc.RootElement.GetProperty("potentialAction");
        Assert.True(actions.GetArrayLength() > 0);
        Assert.Equal("View Run", actions[0].GetProperty("name").GetString());
    }

    [Fact]
    public void BuildTeamsPayload_NoUrl_EmptyActions()
    {
        var payload = CreatePayload();
        payload.RunUrl = null;
        var result = EvalPayloadBuilder.BuildTeamsPayload(payload);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal(0, doc.RootElement.GetProperty("potentialAction").GetArrayLength());
    }

    [Fact]
    public void BuildSlackPayload_Regressions_ExcludesOkSeverity()
    {
        var payload = CreatePayload(blocking: true);
        payload.BaselineComparison!.Details.Add(new RegressionDetail
        {
            MetricName = "stable_metric",
            BaselineValue = 1.0,
            CurrentValue = 1.0,
            Delta = 0,
            Severity = "ok",
        });
        var result = EvalPayloadBuilder.BuildSlackPayload(payload);

        using var doc = JsonDocument.Parse(result);
        var text = doc.RootElement.GetProperty("text").GetString()!;
        Assert.DoesNotContain("stable_metric", text);
        Assert.Contains("latency", text);
    }

    // ── ShouldNotify tests ──

    [Fact]
    public void ShouldNotify_BlockingRegression_ReturnsTrue()
    {
        var payload = CreatePayload(blocking: true);
        Assert.True(EvalPayloadBuilder.ShouldNotify(payload, notifyOnRegressions: true, notifyOnViolations: false));
    }

    [Fact]
    public void ShouldNotify_Violations_ReturnsTrue()
    {
        var payload = CreatePayload(violations: 2);
        Assert.True(EvalPayloadBuilder.ShouldNotify(payload, notifyOnRegressions: false, notifyOnViolations: true));
    }

    [Fact]
    public void ShouldNotify_WarningRegression_ReturnsTrue()
    {
        var payload = CreatePayload();
        payload.BaselineComparison = new BaselineComparison
        {
            HasRegression = true,
            Details = [new RegressionDetail { MetricName = "m", Severity = "warning", BaselineValue = 1, CurrentValue = 2, Delta = 1 }],
        };
        Assert.True(EvalPayloadBuilder.ShouldNotify(payload, notifyOnRegressions: true, notifyOnViolations: false));
    }

    [Fact]
    public void ShouldNotify_BothFlagsDisabled_ReturnsFalse()
    {
        var payload = CreatePayload(blocking: true, violations: 5);
        Assert.False(EvalPayloadBuilder.ShouldNotify(payload, notifyOnRegressions: false, notifyOnViolations: false));
    }

    [Fact]
    public void ShouldNotify_CleanPayload_ReturnsFalse()
    {
        var payload = CreatePayload();
        Assert.False(EvalPayloadBuilder.ShouldNotify(payload, notifyOnRegressions: true, notifyOnViolations: true));
    }
}
