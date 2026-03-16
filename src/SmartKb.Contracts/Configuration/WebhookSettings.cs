namespace SmartKb.Contracts.Configuration;

public sealed class WebhookSettings
{
    public const string SectionName = "Webhook";

    /// <summary>
    /// Base URL for webhook callbacks, e.g. "https://smartkb-api.azurewebsites.net".
    /// Required for registering webhooks with external systems.
    /// </summary>
    public string? BaseCallbackUrl { get; set; }

    /// <summary>
    /// Default polling fallback interval in seconds when webhooks are unavailable. Default: 300 (5 minutes).
    /// </summary>
    public int PollingFallbackIntervalSeconds { get; set; } = 300;

    /// <summary>
    /// Maximum jitter in seconds added to polling interval to prevent thundering herd. Default: 60.
    /// </summary>
    public int PollingJitterMaxSeconds { get; set; } = 60;

    /// <summary>
    /// Number of consecutive failures before activating polling fallback. Default: 3.
    /// </summary>
    public int FailureThresholdForFallback { get; set; } = 3;

    public bool IsConfigured => !string.IsNullOrEmpty(BaseCallbackUrl);
}
