using System.Text.Json.Serialization;

namespace SmartKb.Contracts.Models;

/// <summary>
/// Azure DevOps service hook notification payload.
/// See: https://learn.microsoft.com/en-us/azure/devops/service-hooks/events
/// </summary>
public sealed class AdoWebhookPayload
{
    [JsonPropertyName("subscriptionId")]
    public string? SubscriptionId { get; set; }

    [JsonPropertyName("notificationId")]
    public int NotificationId { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("eventType")]
    public string? EventType { get; set; }

    [JsonPropertyName("publisherId")]
    public string? PublisherId { get; set; }

    [JsonPropertyName("message")]
    public AdoWebhookMessage? Message { get; set; }

    [JsonPropertyName("resource")]
    public AdoWebhookResource? Resource { get; set; }

    [JsonPropertyName("createdDate")]
    public DateTimeOffset? CreatedDate { get; set; }
}

public sealed class AdoWebhookMessage
{
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("html")]
    public string? Html { get; set; }

    [JsonPropertyName("markdown")]
    public string? Markdown { get; set; }
}

public sealed class AdoWebhookResource
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("workItemId")]
    public int WorkItemId { get; set; }

    [JsonPropertyName("rev")]
    public int Rev { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("fields")]
    public Dictionary<string, object?>? Fields { get; set; }

    [JsonPropertyName("revision")]
    public AdoWebhookResourceRevision? Revision { get; set; }
}

public sealed class AdoWebhookResourceRevision
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("rev")]
    public int Rev { get; set; }

    [JsonPropertyName("fields")]
    public Dictionary<string, object?>? Fields { get; set; }
}

/// <summary>
/// ADO service hook subscription creation request body.
/// </summary>
public sealed class AdoServiceHookSubscriptionRequest
{
    [JsonPropertyName("publisherId")]
    public string PublisherId { get; set; } = "tfs";

    [JsonPropertyName("eventType")]
    public required string EventType { get; set; }

    [JsonPropertyName("consumerId")]
    public string ConsumerId { get; set; } = "webHooks";

    [JsonPropertyName("consumerActionId")]
    public string ConsumerActionId { get; set; } = "httpRequest";

    [JsonPropertyName("publisherInputs")]
    public Dictionary<string, string> PublisherInputs { get; set; } = new();

    [JsonPropertyName("consumerInputs")]
    public required Dictionary<string, string> ConsumerInputs { get; set; }
}

/// <summary>
/// ADO service hook subscription response.
/// </summary>
public sealed class AdoServiceHookSubscriptionResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("eventType")]
    public string? EventType { get; set; }
}
