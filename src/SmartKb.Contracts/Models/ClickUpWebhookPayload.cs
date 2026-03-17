using System.Text.Json.Serialization;

namespace SmartKb.Contracts.Models;

/// <summary>
/// ClickUp webhook event payload.
/// ClickUp sends a single event per webhook delivery.
/// See: https://clickup.com/api/developer-portal/webhooks/
/// </summary>
public sealed class ClickUpWebhookEvent
{
    [JsonPropertyName("webhook_id")]
    public string? WebhookId { get; set; }

    [JsonPropertyName("event")]
    public string? Event { get; set; }

    [JsonPropertyName("task_id")]
    public string? TaskId { get; set; }

    [JsonPropertyName("history_items")]
    public List<ClickUpHistoryItem>? HistoryItems { get; set; }
}

public sealed class ClickUpHistoryItem
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("type")]
    public int Type { get; set; }

    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("field")]
    public string? Field { get; set; }

    [JsonPropertyName("after")]
    public object? After { get; set; }

    [JsonPropertyName("before")]
    public object? Before { get; set; }
}

/// <summary>
/// ClickUp webhook creation request for the Webhooks API v2.
/// </summary>
public sealed class ClickUpWebhookCreateRequest
{
    [JsonPropertyName("endpoint")]
    public required string Endpoint { get; set; }

    [JsonPropertyName("events")]
    public required List<string> Events { get; set; }

    [JsonPropertyName("secret")]
    public string? Secret { get; set; }
}

/// <summary>
/// ClickUp webhook creation response.
/// </summary>
public sealed class ClickUpWebhookCreateResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("webhook")]
    public ClickUpWebhookInfo? Webhook { get; set; }
}

public sealed class ClickUpWebhookInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("endpoint")]
    public string? Endpoint { get; set; }

    [JsonPropertyName("events")]
    public List<string>? Events { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }
}
