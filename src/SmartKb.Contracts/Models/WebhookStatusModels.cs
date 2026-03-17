namespace SmartKb.Contracts.Models;

/// <summary>
/// Status of a single webhook subscription for diagnostics display.
/// </summary>
public sealed record WebhookSubscriptionStatus(
    Guid Id,
    Guid ConnectorId,
    string ConnectorName,
    string ConnectorType,
    string EventType,
    bool IsActive,
    bool PollingFallbackActive,
    int ConsecutiveFailures,
    DateTimeOffset? LastDeliveryAt,
    DateTimeOffset? NextPollAt,
    string? ExternalSubscriptionId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Response containing webhook subscriptions for a connector or all connectors.
/// </summary>
public sealed record WebhookStatusListResponse(
    IReadOnlyList<WebhookSubscriptionStatus> Subscriptions,
    int TotalCount,
    int ActiveCount,
    int FallbackCount);

/// <summary>
/// Aggregated diagnostics summary across all connectors for a tenant.
/// </summary>
public sealed record DiagnosticsSummaryResponse(
    int TotalConnectors,
    int EnabledConnectors,
    int DisabledConnectors,
    int TotalWebhooks,
    int ActiveWebhooks,
    int FallbackWebhooks,
    int FailingWebhooks,
    bool ServiceBusConfigured,
    bool KeyVaultConfigured,
    bool OpenAiConfigured,
    bool SearchServiceConfigured,
    ConnectorHealthSummary[] ConnectorHealth);

/// <summary>
/// Per-connector health summary.
/// </summary>
public sealed record ConnectorHealthSummary(
    Guid ConnectorId,
    string Name,
    string ConnectorType,
    string Status,
    string? LastSyncStatus,
    DateTimeOffset? LastSyncAt,
    int WebhookCount,
    int WebhooksInFallback,
    int TotalFailures);
