using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SmartKb.Contracts;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;

namespace SmartKb.Contracts.Tests;

public class WebhookEvalNotificationClientTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static EvalNotificationSettings DefaultSettings(string format = "generic") => new()
    {
        WebhookUrl = "https://hooks.example.com/test",
        Format = format,
        NotifyOnViolations = true,
        NotifyOnRegressions = true,
        TimeoutSeconds = 5,
    };

    private static EvalNotificationPayload BlockingRegressionPayload() => new()
    {
        RunId = "run-001",
        RunType = "nightly",
        TotalCases = 100,
        SuccessfulCases = 90,
        FailedCases = 10,
        HasBlockingRegression = true,
        ViolationCount = 2,
        Violations =
        [
            new EvalViolationDto { MetricName = "groundedness", ActualValue = 0.65f, ThresholdValue = 0.80f, Direction = ThresholdDirection.GreaterThanOrEqual },
            new EvalViolationDto { MetricName = "citationCoverage", ActualValue = 0.50f, ThresholdValue = 0.70f, Direction = ThresholdDirection.GreaterThanOrEqual },
        ],
        BaselineComparison = new EvalBaselineComparisonDto
        {
            HasRegression = true,
            ShouldBlock = true,
            Details =
            [
                new EvalRegressionDetailDto { MetricName = "groundedness", BaselineValue = 0.85f, CurrentValue = 0.65f, Delta = -0.20f, Severity = "critical" },
                new EvalRegressionDetailDto { MetricName = "citationCoverage", BaselineValue = 0.75f, CurrentValue = 0.50f, Delta = -0.25f, Severity = EvalSeverity.Warning },
                new EvalRegressionDetailDto { MetricName = "safetyPassRate", BaselineValue = 1.0f, CurrentValue = 1.0f, Delta = 0f, Severity = EvalSeverity.Ok },
            ],
        },
        RunUrl = "https://ci.example.com/runs/001",
    };

    private static EvalNotificationPayload ViolationOnlyPayload() => new()
    {
        RunId = "run-002",
        RunType = "weekly",
        TotalCases = 50,
        SuccessfulCases = 48,
        FailedCases = 2,
        HasBlockingRegression = false,
        ViolationCount = 1,
        Violations =
        [
            new EvalViolationDto { MetricName = "noEvidenceRate", ActualValue = 0.15f, ThresholdValue = 0.10f, Direction = ThresholdDirection.LessThanOrEqual },
        ],
    };

    private static EvalNotificationPayload CleanPayload() => new()
    {
        RunId = "run-003",
        RunType = "nightly",
        TotalCases = 100,
        SuccessfulCases = 100,
        FailedCases = 0,
        HasBlockingRegression = false,
        ViolationCount = 0,
    };

    private static EvalNotificationPayload BaselineRegressionNoBlockingPayload() => new()
    {
        RunId = "run-004",
        RunType = "nightly",
        TotalCases = 100,
        SuccessfulCases = 95,
        FailedCases = 5,
        HasBlockingRegression = false,
        ViolationCount = 0,
        BaselineComparison = new EvalBaselineComparisonDto
        {
            HasRegression = true,
            ShouldBlock = false,
            Details =
            [
                new EvalRegressionDetailDto { MetricName = "groundedness", BaselineValue = 0.85f, CurrentValue = 0.80f, Delta = -0.05f, Severity = EvalSeverity.Warning },
            ],
        },
    };

    #region NotifyAsync — configuration and routing

    [Fact]
    public async Task NotifyAsync_ReturnsTrue_WhenNotConfigured()
    {
        var settings = new EvalNotificationSettings { WebhookUrl = null };
        var handler = new FakeHandler(HttpStatusCode.OK);
        var client = CreateClient(settings, handler);

        var result = await client.NotifyAsync(BlockingRegressionPayload());

        Assert.True(result);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task NotifyAsync_ReturnsTrue_WhenNoNotificationNeeded()
    {
        var settings = DefaultSettings();
        var handler = new FakeHandler(HttpStatusCode.OK);
        var client = CreateClient(settings, handler);

        var result = await client.NotifyAsync(CleanPayload());

        Assert.True(result);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task NotifyAsync_ReturnsTrue_WhenWebhookSucceeds()
    {
        var settings = DefaultSettings();
        var handler = new FakeHandler(HttpStatusCode.OK);
        var client = CreateClient(settings, handler);

        var result = await client.NotifyAsync(BlockingRegressionPayload());

        Assert.True(result);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task NotifyAsync_ReturnsFalse_WhenWebhookReturns500()
    {
        var settings = DefaultSettings();
        var handler = new FakeHandler(HttpStatusCode.InternalServerError);
        var client = CreateClient(settings, handler);

        var result = await client.NotifyAsync(BlockingRegressionPayload());

        Assert.False(result);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task NotifyAsync_ReturnsFalse_WhenHttpThrows()
    {
        var settings = DefaultSettings();
        var handler = new ThrowingHandler(new HttpRequestException("Connection refused"));
        var client = CreateClient(settings, handler);

        var result = await client.NotifyAsync(BlockingRegressionPayload());

        Assert.False(result);
    }

    [Fact]
    public async Task NotifyAsync_PropagatesCancellation()
    {
        var settings = DefaultSettings();
        var handler = new ThrowingHandler(new OperationCanceledException());
        var client = CreateClient(settings, handler);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.NotifyAsync(BlockingRegressionPayload(), cts.Token));
    }

    [Fact]
    public async Task NotifyAsync_PostsToConfiguredUrl()
    {
        var settings = DefaultSettings();
        var handler = new FakeHandler(HttpStatusCode.OK);
        var client = CreateClient(settings, handler);

        await client.NotifyAsync(BlockingRegressionPayload());

        Assert.Equal("https://hooks.example.com/test", handler.LastRequestUri?.ToString());
    }

    #endregion

    #region ShouldNotify — notification conditions

    [Fact]
    public async Task ShouldNotify_Sends_WhenBlockingRegressionAndNotifyOnRegressionsTrue()
    {
        var settings = DefaultSettings();
        settings.NotifyOnViolations = false;
        var handler = new FakeHandler(HttpStatusCode.OK);
        var client = CreateClient(settings, handler);

        await client.NotifyAsync(BlockingRegressionPayload());

        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task ShouldNotify_Sends_WhenViolationsAndNotifyOnViolationsTrue()
    {
        var settings = DefaultSettings();
        settings.NotifyOnRegressions = false;
        var handler = new FakeHandler(HttpStatusCode.OK);
        var client = CreateClient(settings, handler);

        await client.NotifyAsync(ViolationOnlyPayload());

        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task ShouldNotify_Sends_WhenBaselineRegressionAndNotifyOnRegressionsTrue()
    {
        var settings = DefaultSettings();
        settings.NotifyOnViolations = false;
        var handler = new FakeHandler(HttpStatusCode.OK);
        var client = CreateClient(settings, handler);

        await client.NotifyAsync(BaselineRegressionNoBlockingPayload());

        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task ShouldNotify_DoesNotSend_WhenBothFlagsDisabled()
    {
        var settings = DefaultSettings();
        settings.NotifyOnViolations = false;
        settings.NotifyOnRegressions = false;
        var handler = new FakeHandler(HttpStatusCode.OK);
        var client = CreateClient(settings, handler);

        var result = await client.NotifyAsync(BlockingRegressionPayload());

        Assert.True(result);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task ShouldNotify_DoesNotSend_WhenViolationsDisabledAndNoRegression()
    {
        var settings = DefaultSettings();
        settings.NotifyOnViolations = false;
        var handler = new FakeHandler(HttpStatusCode.OK);
        var client = CreateClient(settings, handler);

        await client.NotifyAsync(ViolationOnlyPayload());

        Assert.Equal(0, handler.RequestCount);
    }

    #endregion

    #region BuildPayload — generic format

    [Fact]
    public async Task BuildGenericPayload_ContainsAllFields()
    {
        var settings = DefaultSettings("generic");
        var handler = new FakeHandler(HttpStatusCode.OK);
        var client = CreateClient(settings, handler);

        var payload = BlockingRegressionPayload();
        await client.NotifyAsync(payload);

        var body = handler.LastRequestBody!;
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.Equal("eval.regression_alert", root.GetProperty("eventType").GetString());
        Assert.Equal("run-001", root.GetProperty("runId").GetString());
        Assert.Equal("nightly", root.GetProperty("runType").GetString());
        Assert.Equal(100, root.GetProperty("totalCases").GetInt32());
        Assert.Equal(90, root.GetProperty("successfulCases").GetInt32());
        Assert.Equal(10, root.GetProperty("failedCases").GetInt32());
        Assert.True(root.GetProperty("hasBlockingRegression").GetBoolean());
        Assert.Equal(2, root.GetProperty("violationCount").GetInt32());
        Assert.Equal(2, root.GetProperty("violations").GetArrayLength());
        Assert.True(root.TryGetProperty("baselineComparison", out _));
        Assert.Equal("https://ci.example.com/runs/001", root.GetProperty("runUrl").GetString());
        Assert.True(root.TryGetProperty("timestamp", out _));
    }

    #endregion

    #region BuildPayload — Slack format

    [Fact]
    public async Task BuildSlackPayload_ContainsBlockingRegressionHeader()
    {
        var settings = DefaultSettings("slack");
        var handler = new FakeHandler(HttpStatusCode.OK);
        var client = CreateClient(settings, handler);

        await client.NotifyAsync(BlockingRegressionPayload());

        var body = handler.LastRequestBody!;
        using var doc = JsonDocument.Parse(body);
        var text = doc.RootElement.GetProperty("text").GetString()!;

        Assert.Contains(":rotating_light:", text);
        Assert.Contains("BLOCKING REGRESSION", text);
        Assert.Contains("`run-001`", text);
        Assert.Contains("100 total", text);
        Assert.Contains("90 success", text);
        Assert.Contains("10 failed", text);
    }

    [Fact]
    public async Task BuildSlackPayload_ContainsViolationDetails()
    {
        var settings = DefaultSettings("slack");
        var handler = new FakeHandler(HttpStatusCode.OK);
        var client = CreateClient(settings, handler);

        await client.NotifyAsync(BlockingRegressionPayload());

        var body = handler.LastRequestBody!;
        using var doc = JsonDocument.Parse(body);
        var text = doc.RootElement.GetProperty("text").GetString()!;

        Assert.Contains("Threshold Violations", text);
        Assert.Contains("groundedness", text);
        Assert.Contains("citationCoverage", text);
    }

    [Fact]
    public async Task BuildSlackPayload_ContainsRegressionDetails()
    {
        var settings = DefaultSettings("slack");
        var handler = new FakeHandler(HttpStatusCode.OK);
        var client = CreateClient(settings, handler);

        await client.NotifyAsync(BlockingRegressionPayload());

        var body = handler.LastRequestBody!;
        using var doc = JsonDocument.Parse(body);
        var text = doc.RootElement.GetProperty("text").GetString()!;

        Assert.Contains("Regressions", text);
        Assert.Contains("groundedness", text);
        Assert.Contains("CRITICAL", text);
        Assert.Contains("WARNING", text);
        // "ok" severity should be excluded
        Assert.DoesNotContain("safetyPassRate", text);
    }

    [Fact]
    public async Task BuildSlackPayload_ContainsRunUrlLink()
    {
        var settings = DefaultSettings("slack");
        var handler = new FakeHandler(HttpStatusCode.OK);
        var client = CreateClient(settings, handler);

        await client.NotifyAsync(BlockingRegressionPayload());

        var body = handler.LastRequestBody!;
        using var doc = JsonDocument.Parse(body);
        var text = doc.RootElement.GetProperty("text").GetString()!;

        Assert.Contains("<https://ci.example.com/runs/001|View Run>", text);
    }

    [Fact]
    public async Task BuildSlackPayload_WarningIcon_WhenNotBlocking()
    {
        var settings = DefaultSettings("slack");
        var handler = new FakeHandler(HttpStatusCode.OK);
        var client = CreateClient(settings, handler);

        await client.NotifyAsync(ViolationOnlyPayload());

        var body = handler.LastRequestBody!;
        using var doc = JsonDocument.Parse(body);
        var text = doc.RootElement.GetProperty("text").GetString()!;

        Assert.Contains(":warning:", text);
        Assert.Contains("Threshold Violations", text);
        Assert.DoesNotContain("BLOCKING REGRESSION", text);
    }

    [Fact]
    public async Task BuildSlackPayload_OmitsRunUrl_WhenNull()
    {
        var settings = DefaultSettings("slack");
        var handler = new FakeHandler(HttpStatusCode.OK);
        var client = CreateClient(settings, handler);

        var payload = ViolationOnlyPayload(); // has no RunUrl
        await client.NotifyAsync(payload);

        var body = handler.LastRequestBody!;
        using var doc = JsonDocument.Parse(body);
        var text = doc.RootElement.GetProperty("text").GetString()!;

        Assert.DoesNotContain("View Run", text);
    }

    #endregion

    #region BuildPayload — Teams format

    [Fact]
    public async Task BuildTeamsPayload_ContainsMessageCardStructure()
    {
        var settings = DefaultSettings("teams");
        var handler = new FakeHandler(HttpStatusCode.OK);
        var client = CreateClient(settings, handler);

        await client.NotifyAsync(BlockingRegressionPayload());

        var body = handler.LastRequestBody!;
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.Equal("MessageCard", root.GetProperty("type").GetString());
        Assert.Equal("FF0000", root.GetProperty("themeColor").GetString());
        Assert.Contains("BLOCKING REGRESSION", root.GetProperty("title").GetString());
        Assert.Contains("BLOCKING REGRESSION", root.GetProperty("summary").GetString());
    }

    [Fact]
    public async Task BuildTeamsPayload_ContainsViolationAndRegressionText()
    {
        var settings = DefaultSettings("teams");
        var handler = new FakeHandler(HttpStatusCode.OK);
        var client = CreateClient(settings, handler);

        await client.NotifyAsync(BlockingRegressionPayload());

        var body = handler.LastRequestBody!;
        using var doc = JsonDocument.Parse(body);
        var text = doc.RootElement.GetProperty("text").GetString()!;

        Assert.Contains("`run-001`", text);
        Assert.Contains("**Threshold Violations:**", text);
        Assert.Contains("**Regressions:**", text);
        Assert.Contains("groundedness", text);
        Assert.DoesNotContain("safetyPassRate", text);
    }

    [Fact]
    public async Task BuildTeamsPayload_IncludesViewRunAction_WhenRunUrlProvided()
    {
        var settings = DefaultSettings("teams");
        var handler = new FakeHandler(HttpStatusCode.OK);
        var client = CreateClient(settings, handler);

        await client.NotifyAsync(BlockingRegressionPayload());

        var body = handler.LastRequestBody!;
        using var doc = JsonDocument.Parse(body);
        var actions = doc.RootElement.GetProperty("potentialAction");

        Assert.Equal(1, actions.GetArrayLength());
        Assert.Equal("OpenUri", actions[0].GetProperty("type").GetString());
        Assert.Equal("View Run", actions[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task BuildTeamsPayload_EmptyActions_WhenNoRunUrl()
    {
        var settings = DefaultSettings("teams");
        var handler = new FakeHandler(HttpStatusCode.OK);
        var client = CreateClient(settings, handler);

        await client.NotifyAsync(ViolationOnlyPayload()); // no RunUrl

        var body = handler.LastRequestBody!;
        using var doc = JsonDocument.Parse(body);
        var actions = doc.RootElement.GetProperty("potentialAction");

        Assert.Equal(0, actions.GetArrayLength());
    }

    [Fact]
    public async Task BuildTeamsPayload_OrangeColor_WhenNotBlocking()
    {
        var settings = DefaultSettings("teams");
        var handler = new FakeHandler(HttpStatusCode.OK);
        var client = CreateClient(settings, handler);

        await client.NotifyAsync(ViolationOnlyPayload());

        var body = handler.LastRequestBody!;
        using var doc = JsonDocument.Parse(body);

        Assert.Equal("FFA500", doc.RootElement.GetProperty("themeColor").GetString());
    }

    #endregion

    #region Format routing — unknown format falls back to generic

    [Fact]
    public async Task BuildPayload_UnknownFormat_FallsBackToGeneric()
    {
        var settings = DefaultSettings("webhook");
        var handler = new FakeHandler(HttpStatusCode.OK);
        var client = CreateClient(settings, handler);

        await client.NotifyAsync(BlockingRegressionPayload());

        var body = handler.LastRequestBody!;
        using var doc = JsonDocument.Parse(body);

        Assert.True(doc.RootElement.TryGetProperty("eventType", out var et));
        Assert.Equal("eval.regression_alert", et.GetString());
    }

    [Fact]
    public async Task BuildPayload_CaseInsensitiveFormat()
    {
        var settings = DefaultSettings("SLACK");
        var handler = new FakeHandler(HttpStatusCode.OK);
        var client = CreateClient(settings, handler);

        await client.NotifyAsync(BlockingRegressionPayload());

        var body = handler.LastRequestBody!;
        using var doc = JsonDocument.Parse(body);

        Assert.True(doc.RootElement.TryGetProperty("text", out _));
    }

    #endregion

    #region Helpers

    private static WebhookEvalNotificationClient CreateClient(
        EvalNotificationSettings settings,
        HttpMessageHandler handler)
    {
        var factory = new FakeHttpClientFactory(handler);
        return new WebhookEvalNotificationClient(
            settings,
            factory,
            NullLogger<WebhookEvalNotificationClient>.Instance);
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public FakeHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        public int RequestCount { get; private set; }
        public Uri? LastRequestUri { get; private set; }
        public string? LastRequestBody { get; private set; }

        public FakeHandler(HttpStatusCode statusCode) => _statusCode = statusCode;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            LastRequestUri = request.RequestUri;
            LastRequestBody = request.Content is not null
                ? await request.Content.ReadAsStringAsync(cancellationToken)
                : null;

            return new HttpResponseMessage(_statusCode);
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        private readonly Exception _exception;
        public ThrowingHandler(Exception exception) => _exception = exception;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException(cancellationToken);
            throw _exception;
        }
    }

    #endregion
}
