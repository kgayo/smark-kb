using System.Net;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Eval.Cli;

namespace SmartKb.Eval.Tests;

public class WebhookEvalNotificationServiceTests
{
    private static EvalNotificationPayload CreatePayload(
        bool hasBlockingRegression = true,
        int violationCount = 2,
        bool hasRegressionDetails = true) => new()
    {
        RunId = "eval-run-20260319-120000",
        RunType = "full",
        TotalCases = 62,
        SuccessfulCases = 58,
        FailedCases = 4,
        HasBlockingRegression = hasBlockingRegression,
        ViolationCount = violationCount,
        Violations = violationCount > 0
            ? new List<EvalViolationDto>
            {
                new() { MetricName = "Groundedness", ActualValue = 0.72f, ThresholdValue = 0.80f, Direction = ">=" },
                new() { MetricName = "CitationCoverage", ActualValue = 0.60f, ThresholdValue = 0.70f, Direction = ">=" },
            }
            : [],
        BaselineComparison = hasRegressionDetails
            ? new EvalBaselineComparisonDto
            {
                HasRegression = true,
                ShouldBlock = hasBlockingRegression,
                Details = new List<EvalRegressionDetailDto>
                {
                    new() { MetricName = "Groundedness", BaselineValue = 0.90f, CurrentValue = 0.72f, Delta = 0.18f, Severity = "blocking" },
                    new() { MetricName = "RoutingAccuracy", BaselineValue = 0.75f, CurrentValue = 0.72f, Delta = 0.03f, Severity = "warning" },
                },
            }
            : null,
        RunUrl = "https://github.com/org/repo/actions/runs/12345",
    };

    [Fact]
    public async Task NotifyAsync_WhenNotConfigured_ReturnsTrue()
    {
        var settings = new EvalNotificationSettings { WebhookUrl = null };
        using var svc = new WebhookEvalNotificationService(settings);

        var result = await svc.NotifyAsync(CreatePayload());

        Assert.True(result);
    }

    [Fact]
    public async Task NotifyAsync_WhenNoIssues_ReturnsTrue()
    {
        var settings = new EvalNotificationSettings { WebhookUrl = "https://hooks.example.com/test" };
        using var svc = new WebhookEvalNotificationService(settings);

        var payload = CreatePayload(hasBlockingRegression: false, violationCount: 0, hasRegressionDetails: false);
        var result = await svc.NotifyAsync(payload);

        Assert.True(result); // No notification needed.
    }

    [Fact]
    public async Task NotifyAsync_WithViolations_SendsWebhook()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var settings = new EvalNotificationSettings { WebhookUrl = "https://hooks.example.com/test" };
        using var svc = new WebhookEvalNotificationService(settings, httpClient);

        var result = await svc.NotifyAsync(CreatePayload());

        Assert.True(result);
        Assert.Single(handler.Requests);
        Assert.Equal("https://hooks.example.com/test", handler.Requests[0].RequestUri?.ToString());
    }

    [Fact]
    public async Task NotifyAsync_OnHttpFailure_ReturnsFalse()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.InternalServerError);
        var httpClient = new HttpClient(handler);
        var settings = new EvalNotificationSettings { WebhookUrl = "https://hooks.example.com/test" };
        using var svc = new WebhookEvalNotificationService(settings, httpClient);

        var result = await svc.NotifyAsync(CreatePayload());

        Assert.False(result);
    }

    [Fact]
    public async Task NotifyAsync_OnException_ReturnsFalse()
    {
        var handler = new ThrowingHttpHandler();
        var httpClient = new HttpClient(handler);
        var settings = new EvalNotificationSettings { WebhookUrl = "https://hooks.example.com/test" };
        using var svc = new WebhookEvalNotificationService(settings, httpClient);

        var result = await svc.NotifyAsync(CreatePayload());

        Assert.False(result);
    }

    [Fact]
    public void ShouldNotify_BlockingRegression_ReturnsTrue()
    {
        var settings = new EvalNotificationSettings
        {
            WebhookUrl = "https://hooks.example.com/test",
            NotifyOnRegressions = true,
        };
        using var svc = new WebhookEvalNotificationService(settings);

        Assert.True(svc.ShouldNotify(CreatePayload(hasBlockingRegression: true)));
    }

    [Fact]
    public void ShouldNotify_ViolationsOnly_ReturnsTrue()
    {
        var settings = new EvalNotificationSettings
        {
            WebhookUrl = "https://hooks.example.com/test",
            NotifyOnViolations = true,
            NotifyOnRegressions = false,
        };
        using var svc = new WebhookEvalNotificationService(settings);

        Assert.True(svc.ShouldNotify(CreatePayload(hasBlockingRegression: false, violationCount: 1)));
    }

    [Fact]
    public void ShouldNotify_NoIssues_ReturnsFalse()
    {
        var settings = new EvalNotificationSettings { WebhookUrl = "https://hooks.example.com/test" };
        using var svc = new WebhookEvalNotificationService(settings);

        Assert.False(svc.ShouldNotify(CreatePayload(hasBlockingRegression: false, violationCount: 0, hasRegressionDetails: false)));
    }

    [Fact]
    public void ShouldNotify_DisabledNotifications_ReturnsFalse()
    {
        var settings = new EvalNotificationSettings
        {
            WebhookUrl = "https://hooks.example.com/test",
            NotifyOnRegressions = false,
            NotifyOnViolations = false,
        };
        using var svc = new WebhookEvalNotificationService(settings);

        Assert.False(svc.ShouldNotify(CreatePayload()));
    }

    [Fact]
    public void BuildPayload_Generic_ContainsEventType()
    {
        var settings = new EvalNotificationSettings
        {
            WebhookUrl = "https://hooks.example.com/test",
            Format = "generic",
        };
        using var svc = new WebhookEvalNotificationService(settings);

        var json = svc.BuildPayload(CreatePayload());

        Assert.Contains("eval.regression_alert", json);
        Assert.Contains("eval-run-20260319-120000", json);
    }

    [Fact]
    public void BuildPayload_Slack_ContainsSlackFormatting()
    {
        var settings = new EvalNotificationSettings
        {
            WebhookUrl = "https://hooks.slack.com/test",
            Format = "slack",
        };
        using var svc = new WebhookEvalNotificationService(settings);

        var json = svc.BuildPayload(CreatePayload());

        Assert.Contains("\"text\":", json);
        Assert.Contains("BLOCKING REGRESSION", json);
        Assert.Contains(":rotating_light:", json);
        Assert.Contains("View Run", json);
    }

    [Fact]
    public void BuildPayload_Teams_ContainsTeamsFormatting()
    {
        var settings = new EvalNotificationSettings
        {
            WebhookUrl = "https://outlook.office.com/webhook/test",
            Format = "teams",
        };
        using var svc = new WebhookEvalNotificationService(settings);

        var json = svc.BuildPayload(CreatePayload());

        Assert.Contains("MessageCard", json);
        Assert.Contains("FF0000", json); // Red for blocking
        Assert.Contains("BLOCKING REGRESSION", json);
    }

    [Fact]
    public void BuildPayload_Slack_WarningLevelUsesWarningIcon()
    {
        var settings = new EvalNotificationSettings
        {
            WebhookUrl = "https://hooks.slack.com/test",
            Format = "slack",
        };
        using var svc = new WebhookEvalNotificationService(settings);

        var payload = CreatePayload(hasBlockingRegression: false);
        var json = svc.BuildPayload(payload);

        Assert.Contains(":warning:", json);
        Assert.Contains("Threshold Violations", json);
    }

    [Fact]
    public void BuildPayload_Teams_WarningLevelUsesOrangeColor()
    {
        var settings = new EvalNotificationSettings
        {
            WebhookUrl = "https://outlook.office.com/webhook/test",
            Format = "teams",
        };
        using var svc = new WebhookEvalNotificationService(settings);

        var payload = CreatePayload(hasBlockingRegression: false);
        var json = svc.BuildPayload(payload);

        Assert.Contains("FFA500", json); // Orange for warning
    }

    [Fact]
    public void EvalNotificationSettings_IsValid_ValidFormats()
    {
        Assert.True(new EvalNotificationSettings { Format = "slack" }.IsValid);
        Assert.True(new EvalNotificationSettings { Format = "teams" }.IsValid);
        Assert.True(new EvalNotificationSettings { Format = "generic" }.IsValid);
        Assert.False(new EvalNotificationSettings { Format = "invalid" }.IsValid);
    }

    [Fact]
    public void EvalNotificationSettings_IsConfigured_RequiresUrl()
    {
        Assert.False(new EvalNotificationSettings().IsConfigured);
        Assert.False(new EvalNotificationSettings { WebhookUrl = "" }.IsConfigured);
        Assert.True(new EvalNotificationSettings { WebhookUrl = "https://hooks.example.com" }.IsConfigured);
    }

    [Fact]
    public async Task NotifyAsync_WarningRegression_NotifiesWhenRegressionsEnabled()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var settings = new EvalNotificationSettings
        {
            WebhookUrl = "https://hooks.example.com/test",
            NotifyOnRegressions = true,
        };
        using var svc = new WebhookEvalNotificationService(settings, httpClient);

        // Non-blocking regression with warning-level details.
        var payload = CreatePayload(hasBlockingRegression: false, violationCount: 0, hasRegressionDetails: true);
        var result = await svc.NotifyAsync(payload);

        Assert.True(result);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public void Dispose_WhenHttpClientInjected_DoesNotDisposeIt()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);
        var settings = new EvalNotificationSettings { WebhookUrl = "https://hooks.example.com/test" };

        var svc = new WebhookEvalNotificationService(settings, httpClient);
        svc.Dispose();

        // Injected HttpClient should still be usable after service disposal.
        Assert.NotNull(httpClient.BaseAddress is null ? "" : ""); // Accessing property doesn't throw.
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
        // If HttpClient were disposed, SendAsync would throw ObjectDisposedException.
        var task = httpClient.SendAsync(request);
        Assert.NotNull(task);

        httpClient.Dispose(); // Caller is responsible for disposal.
    }

    [Fact]
    public void Dispose_WhenHttpClientNotInjected_DisposesOwnedClient()
    {
        var settings = new EvalNotificationSettings { WebhookUrl = "https://hooks.example.com/test" };
        var svc = new WebhookEvalNotificationService(settings);

        // Should not throw — disposes owned HttpClient.
        svc.Dispose();
    }

    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        public List<HttpRequestMessage> Requests { get; } = [];

        public FakeHttpHandler(HttpStatusCode statusCode) => _statusCode = statusCode;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(_statusCode));
        }
    }

    private sealed class ThrowingHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new HttpRequestException("Connection refused");
        }
    }
}
