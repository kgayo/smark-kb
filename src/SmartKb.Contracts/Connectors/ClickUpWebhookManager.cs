using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;

namespace SmartKb.Contracts.Connectors;

/// <summary>
/// Manages ClickUp webhook subscriptions for event-driven ingestion.
/// ClickUp webhooks are registered at the workspace level via the Webhooks API v2.
/// Supports HMAC-SHA256 signature verification.
/// </summary>
public sealed class ClickUpWebhookManager : IWebhookManager
{
    /// <summary>
    /// Supported ClickUp webhook event types.
    /// </summary>
    private static readonly string[] SupportedEventTypes =
    [
        "taskCreated",
        "taskUpdated",
        "taskDeleted",
        "taskStatusUpdated",
        "taskCommentPosted",
    ];

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ClickUpWebhookManager> _logger;

    public ClickUpWebhookManager(
        IHttpClientFactory httpClientFactory,
        ILogger<ClickUpWebhookManager> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public ConnectorType Type => ConnectorType.ClickUp;

    public async Task<IReadOnlyList<WebhookRegistrationResult>> RegisterAsync(
        WebhookRegistrationContext context,
        CancellationToken cancellationToken = default)
    {
        var config = ParseSourceConfig(context.SourceConfig);
        if (config is null)
        {
            _logger.LogWarning("Cannot register ClickUp webhooks: invalid source config for connector {ConnectorId}", context.ConnectorId);
            return [];
        }

        if (string.IsNullOrEmpty(context.SecretValue))
        {
            _logger.LogWarning("Cannot register ClickUp webhooks: no credentials for connector {ConnectorId}", context.ConnectorId);
            return [];
        }

        using var client = CreateHttpClient(config.BaseUrl, context.SecretValue);
        var results = new List<WebhookRegistrationResult>();

        // Generate a shared secret for HMAC-SHA256 signature verification.
        var webhookSecret = GenerateWebhookSecret();

        var callbackUrl = $"{context.CallbackBaseUrl.TrimEnd('/')}/api/webhooks/clickup/{context.ConnectorId}";

        // Determine which events to register.
        var eventTypes = ResolveEventTypes(config);

        // ClickUp registers one webhook with multiple events.
        try
        {
            var webhookRequest = new ClickUpWebhookCreateRequest
            {
                Endpoint = callbackUrl,
                Events = eventTypes,
                Secret = webhookSecret,
            };

            var json = JsonSerializer.Serialize(webhookRequest, SharedJsonOptions.CamelCaseIgnoreNull);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var url = $"api/v2/team/{config.WorkspaceId}/webhook";
            var response = await client.PostAsync(url, content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var result = await DeserializeAsync<ClickUpWebhookCreateResponse>(response, cancellationToken);
                if (result?.Webhook is not null)
                {
                    // Register one result per event type for subscription tracking.
                    foreach (var eventType in eventTypes)
                    {
                        results.Add(new WebhookRegistrationResult(
                            ExternalSubscriptionId: result.Webhook.Id,
                            EventType: eventType,
                            CallbackUrl: callbackUrl,
                            WebhookSecret: webhookSecret));
                    }

                    _logger.LogInformation(
                        "Registered ClickUp webhook: connector={ConnectorId}, webhookId={WebhookId}, events={Events}",
                        context.ConnectorId, result.Webhook.Id, string.Join(",", eventTypes));
                }
            }
            else
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Failed to register ClickUp webhook: connector={ConnectorId}, status={Status}, body={Body}",
                    context.ConnectorId, (int)response.StatusCode,
                    body.Length > 500 ? body[..500] : body);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex,
                "Error registering ClickUp webhook: connector={ConnectorId}",
                context.ConnectorId);
        }

        return results;
    }

    public async Task DeregisterAsync(
        WebhookDeregistrationContext context,
        CancellationToken cancellationToken = default)
    {
        var config = ParseSourceConfig(context.SourceConfig);
        if (config is null || string.IsNullOrEmpty(context.SecretValue))
        {
            _logger.LogWarning("Cannot deregister ClickUp webhooks: invalid config or credentials for connector {ConnectorId}",
                context.ConnectorId);
            return;
        }

        using var client = CreateHttpClient(config.BaseUrl, context.SecretValue);

        // Deduplicate subscription IDs (ClickUp uses one webhook ID for all events).
        var uniqueIds = context.ExternalSubscriptionIds.Distinct().ToList();

        foreach (var webhookId in uniqueIds)
        {
            try
            {
                var url = $"api/v2/webhook/{webhookId}";
                var response = await client.DeleteAsync(url, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation(
                        "Deregistered ClickUp webhook: connector={ConnectorId}, webhookId={WebhookId}",
                        context.ConnectorId, webhookId);
                }
                else
                {
                    _logger.LogWarning(
                        "Failed to deregister ClickUp webhook: connector={ConnectorId}, webhookId={WebhookId}, status={Status}",
                        context.ConnectorId, webhookId, (int)response.StatusCode);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _logger.LogWarning(ex,
                    "Error deregistering ClickUp webhook: connector={ConnectorId}, webhookId={WebhookId}",
                    context.ConnectorId, webhookId);
            }
        }
    }

    /// <summary>
    /// Validates the ClickUp webhook HMAC-SHA256 signature.
    /// ClickUp signs the request body with the webhook secret.
    /// Signature = HMAC-SHA256(secret, requestBody). Sent in X-Signature header.
    /// </summary>
    public static bool ValidateSignature(string requestBody, string? signatureHeader, string? expectedSecret, ILogger? logger = null)
    {
        if (string.IsNullOrEmpty(expectedSecret))
            return true; // No secret configured = skip verification.

        if (string.IsNullOrEmpty(signatureHeader))
            return false;

        try
        {
            var keyBytes = Encoding.UTF8.GetBytes(expectedSecret);
            var bodyBytes = Encoding.UTF8.GetBytes(requestBody);

            var hmac = HMACSHA256.HashData(keyBytes, bodyBytes);
            var computed = Convert.ToHexString(hmac).ToLowerInvariant();

            var expected = signatureHeader.ToLowerInvariant();
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(computed),
                Encoding.UTF8.GetBytes(expected));
        }
        catch (FormatException ex)
        {
            logger?.LogWarning(ex, "ClickUp webhook signature validation failed due to malformed format");
            return false;
        }
    }

    internal static string GenerateWebhookSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes);
    }

    internal static List<string> ResolveEventTypes(ClickUpSourceConfig config)
    {
        var events = new List<string>();

        if (config.IngestTasks)
        {
            events.AddRange(SupportedEventTypes);
        }

        return events.Count > 0 ? events : SupportedEventTypes.ToList();
    }

    private HttpClient CreateHttpClient(string baseUrl, string token)
    {
        var client = _httpClientFactory.CreateClient("ClickUp");
        client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Accept.Add(
            new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private ClickUpSourceConfig? ParseSourceConfig(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<ClickUpSourceConfig>(json, SharedJsonOptions.CamelCaseIgnoreNull); }
        catch (JsonException ex) { _logger.LogWarning(ex, "Failed to deserialize ClickUpSourceConfig from JSON"); return null; }
    }

    private static Task<T?> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken ct)
        => ConnectorHttpHelper.DeserializeAsync<T>(response, SharedJsonOptions.CamelCaseIgnoreNull, ct);
}
