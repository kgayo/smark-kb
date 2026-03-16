using System.Text.Json.Serialization;

namespace SmartKb.Contracts.Models;

/// <summary>
/// Microsoft Graph change notification payload.
/// See: https://learn.microsoft.com/en-us/graph/api/resources/changenotification
/// </summary>
public sealed class GraphChangeNotificationPayload
{
    [JsonPropertyName("value")]
    public List<GraphChangeNotification>? Value { get; set; }
}

public sealed class GraphChangeNotification
{
    [JsonPropertyName("subscriptionId")]
    public string? SubscriptionId { get; set; }

    [JsonPropertyName("clientState")]
    public string? ClientState { get; set; }

    [JsonPropertyName("changeType")]
    public string? ChangeType { get; set; }

    [JsonPropertyName("resource")]
    public string? Resource { get; set; }

    [JsonPropertyName("subscriptionExpirationDateTime")]
    public DateTimeOffset? SubscriptionExpirationDateTime { get; set; }

    [JsonPropertyName("resourceData")]
    public GraphResourceData? ResourceData { get; set; }

    [JsonPropertyName("tenantId")]
    public string? TenantId { get; set; }
}

public sealed class GraphResourceData
{
    [JsonPropertyName("@odata.type")]
    public string? OdataType { get; set; }

    [JsonPropertyName("@odata.id")]
    public string? OdataId { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }
}

/// <summary>
/// Graph subscription creation/update request body.
/// </summary>
public sealed class GraphSubscriptionRequest
{
    [JsonPropertyName("changeType")]
    public required string ChangeType { get; set; }

    [JsonPropertyName("notificationUrl")]
    public required string NotificationUrl { get; set; }

    [JsonPropertyName("resource")]
    public required string Resource { get; set; }

    [JsonPropertyName("expirationDateTime")]
    public required DateTimeOffset ExpirationDateTime { get; set; }

    [JsonPropertyName("clientState")]
    public required string ClientState { get; set; }
}

/// <summary>
/// Graph subscription response.
/// </summary>
public sealed class GraphSubscriptionResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("resource")]
    public string? Resource { get; set; }

    [JsonPropertyName("changeType")]
    public string? ChangeType { get; set; }

    [JsonPropertyName("expirationDateTime")]
    public DateTimeOffset? ExpirationDateTime { get; set; }

    [JsonPropertyName("clientState")]
    public string? ClientState { get; set; }
}
