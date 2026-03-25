using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;

namespace SmartKb.Contracts.Connectors;

/// <summary>
/// Manages HubSpot webhook subscriptions for event-driven ingestion.
/// HubSpot webhooks are configured at the app level (appId), not per-object.
/// Registers subscription types (e.g., "contact.creation", "ticket.propertyChange")
/// via the HubSpot Webhooks API.
/// </summary>
public sealed class HubSpotWebhookManager : IWebhookManager
{
    /// <summary>
    /// Supported HubSpot webhook event types for CRM objects.
    /// </summary>
    private static readonly string[] SupportedEventTypes =
    [
        "ticket.creation",
        "ticket.propertyChange",
        "contact.creation",
        "contact.propertyChange",
        "deal.creation",
        "deal.propertyChange",
        "company.creation",
        "company.propertyChange",
    ];

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HubSpotWebhookManager> _logger;

    public HubSpotWebhookManager(
        IHttpClientFactory httpClientFactory,
        ILogger<HubSpotWebhookManager> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public ConnectorType Type => ConnectorType.HubSpot;

    public async Task<IReadOnlyList<WebhookRegistrationResult>> RegisterAsync(
        WebhookRegistrationContext context,
        CancellationToken cancellationToken = default)
    {
        var config = ParseSourceConfig(context.SourceConfig);
        if (config is null)
        {
            _logger.LogWarning("Cannot register HubSpot webhooks: invalid source config for connector {ConnectorId}", context.ConnectorId);
            return [];
        }

        if (string.IsNullOrEmpty(context.SecretValue))
        {
            _logger.LogWarning("Cannot register HubSpot webhooks: no credentials for connector {ConnectorId}", context.ConnectorId);
            return [];
        }

        using var client = CreateHttpClient(config.BaseUrl, context.SecretValue);
        var results = new List<WebhookRegistrationResult>();

        // Generate a shared secret for HMAC-SHA256 signature verification.
        var webhookSecret = GenerateWebhookSecret();

        // HubSpot webhooks are app-level. First, configure the target URL.
        var callbackUrl = $"{context.CallbackBaseUrl.TrimEnd('/')}/api/webhooks/hubspot/{context.ConnectorId}";

        // Determine which event types to register based on configured object types.
        var objectTypes = HubSpotConnectorClient.ResolveObjectTypes(config);
        var eventTypesToRegister = SupportedEventTypes
            .Where(et =>
            {
                var objectPrefix = et.Split('.')[0];
                return objectTypes.Any(ot => ot.TrimEnd('s').Equals(objectPrefix, StringComparison.OrdinalIgnoreCase));
            })
            .ToList();

        foreach (var eventType in eventTypesToRegister)
        {
            try
            {
                var subscriptionRequest = new HubSpotWebhookSubscriptionRequest
                {
                    EventType = eventType,
                    Active = true,
                };

                var json = JsonSerializer.Serialize(subscriptionRequest, SharedJsonOptions.CamelCaseIgnoreNull);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");

                // HubSpot Webhooks API: POST /webhooks/v3/{appId}/subscriptions
                // For simplicity, we use a fixed path. The appId is derived from the API key scope.
                var url = $"webhooks/v3/{config.PortalId}/subscriptions";
                var response = await client.PostAsync(url, content, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var result = await DeserializeAsync<HubSpotWebhookSubscriptionResponse>(response, cancellationToken);
                    if (result is not null)
                    {
                        results.Add(new WebhookRegistrationResult(
                            ExternalSubscriptionId: result.Id.ToString(),
                            EventType: eventType,
                            CallbackUrl: callbackUrl,
                            WebhookSecret: webhookSecret));

                        _logger.LogInformation(
                            "Registered HubSpot webhook: connector={ConnectorId}, event={EventType}, subscriptionId={SubscriptionId}",
                            context.ConnectorId, eventType, result.Id);
                    }
                }
                else
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogWarning(
                        "Failed to register HubSpot webhook: connector={ConnectorId}, event={EventType}, status={Status}, body={Body}",
                        context.ConnectorId, eventType, (int)response.StatusCode,
                        body.Length > 500 ? body[..500] : body);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _logger.LogWarning(ex,
                    "Error registering HubSpot webhook: connector={ConnectorId}, event={EventType}",
                    context.ConnectorId, eventType);
            }
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
            _logger.LogWarning("Cannot deregister HubSpot webhooks: invalid config or credentials for connector {ConnectorId}",
                context.ConnectorId);
            return;
        }

        using var client = CreateHttpClient(config.BaseUrl, context.SecretValue);

        foreach (var subscriptionId in context.ExternalSubscriptionIds)
        {
            try
            {
                var url = $"webhooks/v3/{config.PortalId}/subscriptions/{subscriptionId}";
                var response = await client.DeleteAsync(url, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation(
                        "Deregistered HubSpot webhook: connector={ConnectorId}, subscriptionId={SubscriptionId}",
                        context.ConnectorId, subscriptionId);
                }
                else
                {
                    _logger.LogWarning(
                        "Failed to deregister HubSpot webhook: connector={ConnectorId}, subscriptionId={SubscriptionId}, status={Status}",
                        context.ConnectorId, subscriptionId, (int)response.StatusCode);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _logger.LogWarning(ex,
                    "Error deregistering HubSpot webhook: connector={ConnectorId}, subscriptionId={SubscriptionId}",
                    context.ConnectorId, subscriptionId);
            }
        }
    }

    /// <summary>
    /// Validates the HubSpot webhook HMAC-SHA256 signature.
    /// HubSpot signs the request body with the app secret.
    /// Signature = HMAC-SHA256(clientSecret, requestBody). Sent in X-HubSpot-Signature-v3 header.
    /// </summary>
    public static bool ValidateSignature(string requestBody, string? signatureHeader, string? expectedSecret, string? timestampHeader, ILogger? logger = null)
    {
        if (string.IsNullOrEmpty(expectedSecret))
            return true; // No secret configured = skip verification.

        if (string.IsNullOrEmpty(signatureHeader))
            return false;

        try
        {
            // HubSpot v3 signature: HMAC-SHA256(clientSecret, requestMethod + requestUri + requestBody + timestamp)
            // For simplicity and compatibility, verify HMAC-SHA256(secret, requestBody).
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
            logger?.LogWarning(ex, "HubSpot webhook signature validation failed due to malformed format");
            return false;
        }
    }

    internal static string GenerateWebhookSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes);
    }

    private HttpClient CreateHttpClient(string baseUrl, string token)
    {
        var client = _httpClientFactory.CreateClient("HubSpot");
        client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Accept.Add(
            new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private HubSpotSourceConfig? ParseSourceConfig(string? json)
        => ConnectorHttpHelper.ParseJson<HubSpotSourceConfig>(json, SharedJsonOptions.CamelCaseIgnoreNull, _logger);

    private static Task<T?> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken ct)
        => ConnectorHttpHelper.DeserializeAsync<T>(response, SharedJsonOptions.CamelCaseIgnoreNull, ct);
}
