using System.Text;
using System.Text.Json;
using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

/// <summary>
/// Shared payload builder for eval regression alert notifications.
/// Used by both WebhookEvalNotificationClient (API-side) and WebhookEvalNotificationService (Eval CLI).
/// </summary>
public static class EvalPayloadBuilder
{
    public static bool ShouldNotify(EvalNotificationPayload payload, bool notifyOnRegressions, bool notifyOnViolations)
    {
        if (notifyOnRegressions && payload.HasBlockingRegression)
            return true;
        if (notifyOnViolations && payload.ViolationCount > 0)
            return true;
        // Also notify on warning-level regressions if regression notifications are on.
        if (notifyOnRegressions && payload.BaselineComparison?.HasRegression == true)
            return true;
        return false;
    }

    public static string BuildPayload(string format, EvalNotificationPayload payload) =>
        string.Equals(format, "slack", StringComparison.OrdinalIgnoreCase) ? BuildSlackPayload(payload) :
        string.Equals(format, "teams", StringComparison.OrdinalIgnoreCase) ? BuildTeamsPayload(payload) :
        BuildGenericPayload(payload);

    public static string BuildGenericPayload(EvalNotificationPayload payload)
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
        return JsonSerializer.Serialize(obj, SharedJsonOptions.CamelCaseCompact);
    }

    public static string BuildSlackPayload(EvalNotificationPayload payload)
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
        return JsonSerializer.Serialize(slackObj, SharedJsonOptions.CamelCaseCompact);
    }

    public static string BuildTeamsPayload(EvalNotificationPayload payload)
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
        return JsonSerializer.Serialize(teamsObj, SharedJsonOptions.CamelCaseCompact);
    }
}
