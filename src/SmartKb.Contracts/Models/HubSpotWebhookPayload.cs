using System.Text.Json.Serialization;

namespace SmartKb.Contracts.Models;

/// <summary>
/// HubSpot webhook event payload.
/// HubSpot sends an array of events per webhook delivery.
/// See: https://developers.hubspot.com/docs/api/webhooks
/// </summary>
public sealed class HubSpotWebhookEvent
{
    [JsonPropertyName("eventId")]
    public long EventId { get; set; }

    [JsonPropertyName("subscriptionId")]
    public long SubscriptionId { get; set; }

    [JsonPropertyName("portalId")]
    public long PortalId { get; set; }

    [JsonPropertyName("appId")]
    public long AppId { get; set; }

    [JsonPropertyName("occurredAt")]
    public long OccurredAt { get; set; }

    [JsonPropertyName("subscriptionType")]
    public string? SubscriptionType { get; set; }

    [JsonPropertyName("attemptNumber")]
    public int AttemptNumber { get; set; }

    [JsonPropertyName("objectId")]
    public long ObjectId { get; set; }

    [JsonPropertyName("changeSource")]
    public string? ChangeSource { get; set; }

    [JsonPropertyName("propertyName")]
    public string? PropertyName { get; set; }

    [JsonPropertyName("propertyValue")]
    public string? PropertyValue { get; set; }
}

/// <summary>
/// HubSpot webhook subscription creation request for the Webhooks API.
/// </summary>
public sealed class HubSpotWebhookSubscriptionRequest
{
    [JsonPropertyName("eventType")]
    public required string EventType { get; set; }

    [JsonPropertyName("propertyName")]
    public string? PropertyName { get; set; }

    [JsonPropertyName("active")]
    public bool Active { get; set; } = true;
}

/// <summary>
/// HubSpot webhook subscription response.
/// </summary>
public sealed class HubSpotWebhookSubscriptionResponse
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("eventType")]
    public string? EventType { get; set; }

    [JsonPropertyName("active")]
    public bool Active { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset? CreatedAt { get; set; }
}

/// <summary>
/// HubSpot webhook settings response (app-level).
/// </summary>
public sealed class HubSpotWebhookSettingsResponse
{
    [JsonPropertyName("targetUrl")]
    public string? TargetUrl { get; set; }

    [JsonPropertyName("throttling")]
    public HubSpotThrottling? Throttling { get; set; }
}

public sealed class HubSpotThrottling
{
    [JsonPropertyName("maxConcurrentRequests")]
    public int MaxConcurrentRequests { get; set; }

    [JsonPropertyName("period")]
    public string? Period { get; set; }
}
