using SmartKb.Contracts.Enums;

namespace SmartKb.Contracts.Services;

/// <summary>
/// Manages webhook subscription lifecycle for a specific connector type.
/// Responsible for registering/deregistering webhooks with external systems.
/// </summary>
public interface IWebhookManager
{
    ConnectorType Type { get; }

    /// <summary>
    /// Registers webhook subscriptions with the external system for the given connector.
    /// Returns the registered subscription details, or empty if webhooks are unavailable.
    /// </summary>
    Task<IReadOnlyList<WebhookRegistrationResult>> RegisterAsync(
        WebhookRegistrationContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deregisters all webhook subscriptions for the given connector from the external system.
    /// </summary>
    Task DeregisterAsync(
        WebhookDeregistrationContext context,
        CancellationToken cancellationToken = default);
}

public sealed record WebhookRegistrationContext(
    Guid ConnectorId,
    string TenantId,
    string? SourceConfig,
    string? SecretValue,
    string CallbackBaseUrl);

public sealed record WebhookDeregistrationContext(
    Guid ConnectorId,
    string TenantId,
    string? SourceConfig,
    string? SecretValue,
    IReadOnlyList<string> ExternalSubscriptionIds);

public sealed record WebhookRegistrationResult(
    string ExternalSubscriptionId,
    string EventType,
    string CallbackUrl,
    string? WebhookSecret);
