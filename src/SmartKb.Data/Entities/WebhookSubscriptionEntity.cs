using SmartKb.Contracts.Enums;

namespace SmartKb.Data.Entities;

public sealed class WebhookSubscriptionEntity
{
    public Guid Id { get; set; }
    public Guid ConnectorId { get; set; }
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// The external subscription ID returned by the source system (e.g., ADO service hook subscription ID).
    /// </summary>
    public string? ExternalSubscriptionId { get; set; }

    /// <summary>
    /// The event type this subscription covers (e.g., "workitem.updated", "workitem.created").
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// The webhook callback URL registered with the source system.
    /// </summary>
    public string CallbackUrl { get; set; } = string.Empty;

    /// <summary>
    /// Key Vault secret name for the HMAC shared secret used for signature verification.
    /// </summary>
    public string? WebhookSecretName { get; set; }

    /// <summary>
    /// Whether the subscription is currently active on the source system.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Whether webhook delivery has failed and polling fallback is active.
    /// </summary>
    public bool PollingFallbackActive { get; set; }

    /// <summary>
    /// Number of consecutive delivery failures. Resets on successful delivery.
    /// </summary>
    public int ConsecutiveFailures { get; set; }

    /// <summary>
    /// Last successful webhook delivery timestamp.
    /// </summary>
    public DateTimeOffset? LastDeliveryAt { get; set; }

    /// <summary>
    /// Next scheduled polling time (only set when polling fallback is active).
    /// </summary>
    public DateTimeOffset? NextPollAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ConnectorEntity Connector { get; set; } = null!;
}
