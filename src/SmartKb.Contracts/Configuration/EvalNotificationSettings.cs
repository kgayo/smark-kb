namespace SmartKb.Contracts.Configuration;

/// <summary>
/// Configuration for eval regression alert notifications (P3-007).
/// Supports Slack Incoming Webhooks, Teams Incoming Webhooks, and generic webhook endpoints.
/// </summary>
public sealed class EvalNotificationSettings
{
    public const string SectionName = "EvalNotification";

    /// <summary>
    /// Webhook URL to send notifications to (Slack, Teams, or generic HTTP endpoint).
    /// When null/empty, notifications are disabled.
    /// </summary>
    public string? WebhookUrl { get; set; }

    /// <summary>
    /// Webhook payload format. Determines how the notification body is structured.
    /// Supported values: "slack", "teams", "generic". Default: "generic".
    /// </summary>
    public string Format { get; set; } = "generic";

    /// <summary>
    /// Whether to send notifications on threshold violations (not just regressions). Default: true.
    /// </summary>
    public bool NotifyOnViolations { get; set; } = true;

    /// <summary>
    /// Whether to send notifications on blocking regressions. Default: true.
    /// </summary>
    public bool NotifyOnRegressions { get; set; } = true;

    /// <summary>
    /// HTTP timeout in seconds for the webhook call. Default: 15.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 15;

    /// <summary>Whether a webhook URL is configured and notifications can be sent.</summary>
    public bool IsConfigured => !string.IsNullOrEmpty(WebhookUrl);

    /// <summary>Validates format is one of the supported values.</summary>
    public bool IsValid => Format is "slack" or "teams" or "generic";
}
