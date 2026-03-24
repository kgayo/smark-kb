using System.Text;
using System.Text.Json;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Models;
using Microsoft.Extensions.Logging;

namespace SmartKb.Contracts.Services;

/// <summary>
/// API-side webhook notification client for eval regression alerts (P3-007).
/// Uses IHttpClientFactory for proper HttpClient lifecycle management.
/// </summary>
public sealed class WebhookEvalNotificationClient : IEvalNotificationService
{
    private readonly EvalNotificationSettings _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WebhookEvalNotificationClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public WebhookEvalNotificationClient(
        EvalNotificationSettings settings,
        IHttpClientFactory httpClientFactory,
        ILogger<WebhookEvalNotificationClient> logger)
    {
        _settings = settings;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<bool> NotifyAsync(EvalNotificationPayload payload, CancellationToken ct = default)
    {
        if (!_settings.IsConfigured)
            return true;

        if (!ShouldNotify(payload))
            return true;

        var body = BuildPayload(payload);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        try
        {
            var client = _httpClientFactory.CreateClient("EvalNotification");
            client.Timeout = TimeSpan.FromSeconds(_settings.TimeoutSeconds);

            var response = await client.PostAsync(_settings.WebhookUrl, content, ct);

            if (response.IsSuccessStatusCode)
            {
                Diagnostics.EvalNotificationsSentTotal.Add(1);
                _logger.LogInformation(
                    "Eval notification sent. RunId={RunId}, Violations={Violations}, Blocking={Blocking}",
                    payload.RunId, payload.ViolationCount, payload.HasBlockingRegression);
                return true;
            }

            Diagnostics.EvalNotificationFailuresTotal.Add(1);
            _logger.LogWarning(
                "Eval notification failed. RunId={RunId}, StatusCode={StatusCode}",
                payload.RunId, (int)response.StatusCode);
            return false;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            Diagnostics.EvalNotificationFailuresTotal.Add(1);
            _logger.LogWarning(ex,
                "Eval notification failed with exception. RunId={RunId}", payload.RunId);
            return false;
        }
    }

    private bool ShouldNotify(EvalNotificationPayload payload)
    {
        if (_settings.NotifyOnRegressions && payload.HasBlockingRegression)
            return true;
        if (_settings.NotifyOnViolations && payload.ViolationCount > 0)
            return true;
        if (_settings.NotifyOnRegressions && payload.BaselineComparison?.HasRegression == true)
            return true;
        return false;
    }

    private string BuildPayload(EvalNotificationPayload payload) =>
        _settings.Format.ToLowerInvariant() switch
        {
            "slack" => BuildSlackPayload(payload),
            "teams" => BuildTeamsPayload(payload),
            _ => BuildGenericPayload(payload),
        };

    private static string BuildGenericPayload(EvalNotificationPayload payload)
    {
        var obj = new
        {
            eventType = "eval.regression_alert",
            runId = payload.RunId,
            runType = payload.RunType,
            totalCases = payload.TotalCases,
            successfulCases = payload.SuccessfulCases,
            failedCases = payload.FailedCases,
            hasBlockingRegression = payload.HasBlockingRegression,
            violationCount = payload.ViolationCount,
            violations = payload.Violations,
            baselineComparison = payload.BaselineComparison,
            runUrl = payload.RunUrl,
            timestamp = DateTimeOffset.UtcNow,
        };
        return JsonSerializer.Serialize(obj, JsonOptions);
    }

    private static string BuildSlackPayload(EvalNotificationPayload payload)
    {
        var icon = payload.HasBlockingRegression ? ":rotating_light:" : ":warning:";
        var severity = payload.HasBlockingRegression ? "BLOCKING REGRESSION" : "Threshold Violations";

        var sb = new StringBuilder();
        sb.AppendLine($"{icon} *Eval Alert — {severity}*");
        sb.AppendLine();
        sb.AppendLine($"*Run:* `{payload.RunId}` ({payload.RunType})");
        sb.AppendLine($"*Cases:* {payload.TotalCases} total, {payload.SuccessfulCases} success, {payload.FailedCases} failed");

        if (payload.Violations is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("*Threshold Violations:*");
            foreach (var v in payload.Violations)
                sb.AppendLine($"• {v.MetricName}: {v.ActualValue:F3} (threshold {v.Direction} {v.ThresholdValue:F3})");
        }

        if (payload.BaselineComparison is { HasRegression: true })
        {
            sb.AppendLine();
            sb.AppendLine("*Regressions:*");
            foreach (var d in payload.BaselineComparison.Details.Where(d => d.Severity is not "ok"))
                sb.AppendLine($"• {d.MetricName}: {d.BaselineValue:F3} → {d.CurrentValue:F3} (Δ{d.Delta:+0.000;-0.000}) [{d.Severity.ToUpperInvariant()}]");
        }

        if (!string.IsNullOrEmpty(payload.RunUrl))
        {
            sb.AppendLine();
            sb.AppendLine($"<{payload.RunUrl}|View Run>");
        }

        var slackObj = new { text = sb.ToString().TrimEnd() };
        return JsonSerializer.Serialize(slackObj, JsonOptions);
    }

    private static string BuildTeamsPayload(EvalNotificationPayload payload)
    {
        var severity = payload.HasBlockingRegression ? "BLOCKING REGRESSION" : "Threshold Violations";
        var color = payload.HasBlockingRegression ? "FF0000" : "FFA500";

        var sb = new StringBuilder();
        sb.AppendLine($"**Run:** `{payload.RunId}` ({payload.RunType})");
        sb.AppendLine();
        sb.AppendLine($"**Cases:** {payload.TotalCases} total, {payload.SuccessfulCases} success, {payload.FailedCases} failed");

        if (payload.Violations is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("**Threshold Violations:**");
            foreach (var v in payload.Violations)
                sb.AppendLine($"- {v.MetricName}: {v.ActualValue:F3} (threshold {v.Direction} {v.ThresholdValue:F3})");
        }

        if (payload.BaselineComparison is { HasRegression: true })
        {
            sb.AppendLine();
            sb.AppendLine("**Regressions:**");
            foreach (var d in payload.BaselineComparison.Details.Where(d => d.Severity is not "ok"))
                sb.AppendLine($"- {d.MetricName}: {d.BaselineValue:F3} → {d.CurrentValue:F3} (Δ{d.Delta:+0.000;-0.000}) [{d.Severity.ToUpperInvariant()}]");
        }

        var teamsObj = new
        {
            type = "MessageCard",
            themeColor = color,
            summary = $"Eval Alert — {severity}",
            title = $"Eval Alert — {severity}",
            text = sb.ToString().TrimEnd(),
            potentialAction = !string.IsNullOrEmpty(payload.RunUrl)
                ? new object[] { new { type = "OpenUri", name = "View Run", targets = new[] { new { os = "default", uri = payload.RunUrl } } } }
                : Array.Empty<object>(),
        };
        return JsonSerializer.Serialize(teamsObj, JsonOptions);
    }
}
