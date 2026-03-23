using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;

namespace SmartKb.Contracts.Connectors;

/// <summary>
/// Manages Azure DevOps service hook subscriptions for webhook-driven ingestion.
/// Registers workitem.created and workitem.updated hooks via the ADO Service Hooks REST API.
/// </summary>
public sealed class AdoWebhookManager : IWebhookManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static readonly string[] SupportedEventTypes =
    [
        "workitem.created",
        "workitem.updated",
    ];

    private const string ApiVersion = "7.1";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AdoWebhookManager> _logger;

    public AdoWebhookManager(
        IHttpClientFactory httpClientFactory,
        ILogger<AdoWebhookManager> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public ConnectorType Type => ConnectorType.AzureDevOps;

    public async Task<IReadOnlyList<WebhookRegistrationResult>> RegisterAsync(
        WebhookRegistrationContext context,
        CancellationToken cancellationToken = default)
    {
        var config = ParseSourceConfig(context.SourceConfig);
        if (config is null)
        {
            _logger.LogWarning("Cannot register ADO webhooks: invalid source config for connector {ConnectorId}", context.ConnectorId);
            return [];
        }

        if (string.IsNullOrEmpty(context.SecretValue))
        {
            _logger.LogWarning("Cannot register ADO webhooks: no credentials for connector {ConnectorId}", context.ConnectorId);
            return [];
        }

        using var client = CreateHttpClient(config.OrganizationUrl, context.SecretValue);
        var results = new List<WebhookRegistrationResult>();

        // Generate a shared secret for HMAC signature verification.
        var webhookSecret = GenerateWebhookSecret();

        foreach (var eventType in SupportedEventTypes)
        {
            try
            {
                var callbackUrl = $"{context.CallbackBaseUrl.TrimEnd('/')}/api/webhooks/ado/{context.ConnectorId}";

                var subscriptionRequest = new AdoServiceHookSubscriptionRequest
                {
                    EventType = eventType,
                    ConsumerInputs = new Dictionary<string, string>
                    {
                        ["url"] = callbackUrl,
                        ["httpHeaders"] = $"X-SmartKb-Tenant:{context.TenantId}",
                        ["basicAuthUsername"] = "",
                        ["basicAuthPassword"] = webhookSecret,
                    },
                };

                // Filter to specific projects if configured.
                if (config.Projects.Count > 0)
                {
                    subscriptionRequest.PublisherInputs["projectId"] = config.Projects[0];
                }

                var json = JsonSerializer.Serialize(subscriptionRequest, JsonOptions);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var url = $"_apis/hooks/subscriptions?api-version={ApiVersion}";
                var response = await client.PostAsync(url, content, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var result = await DeserializeAsync<AdoServiceHookSubscriptionResponse>(response, cancellationToken);
                    if (result?.Id is not null)
                    {
                        results.Add(new WebhookRegistrationResult(
                            ExternalSubscriptionId: result.Id,
                            EventType: eventType,
                            CallbackUrl: callbackUrl,
                            WebhookSecret: webhookSecret));

                        _logger.LogInformation(
                            "Registered ADO service hook: connector={ConnectorId}, event={EventType}, subscriptionId={SubscriptionId}",
                            context.ConnectorId, eventType, result.Id);
                    }
                }
                else
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogWarning(
                        "Failed to register ADO service hook: connector={ConnectorId}, event={EventType}, status={Status}, body={Body}",
                        context.ConnectorId, eventType, (int)response.StatusCode,
                        body.Length > 500 ? body[..500] : body);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _logger.LogWarning(ex,
                    "Error registering ADO service hook: connector={ConnectorId}, event={EventType}",
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
            _logger.LogWarning("Cannot deregister ADO webhooks: invalid config or credentials for connector {ConnectorId}",
                context.ConnectorId);
            return;
        }

        using var client = CreateHttpClient(config.OrganizationUrl, context.SecretValue);

        foreach (var subscriptionId in context.ExternalSubscriptionIds)
        {
            try
            {
                var url = $"_apis/hooks/subscriptions/{subscriptionId}?api-version={ApiVersion}";
                var response = await client.DeleteAsync(url, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation(
                        "Deregistered ADO service hook: connector={ConnectorId}, subscriptionId={SubscriptionId}",
                        context.ConnectorId, subscriptionId);
                }
                else
                {
                    _logger.LogWarning(
                        "Failed to deregister ADO service hook: connector={ConnectorId}, subscriptionId={SubscriptionId}, status={Status}",
                        context.ConnectorId, subscriptionId, (int)response.StatusCode);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _logger.LogWarning(ex,
                    "Error deregistering ADO service hook: connector={ConnectorId}, subscriptionId={SubscriptionId}",
                    context.ConnectorId, subscriptionId);
            }
        }
    }

    internal static string GenerateWebhookSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes);
    }

    private HttpClient CreateHttpClient(string organizationUrl, string pat)
    {
        var client = _httpClientFactory.CreateClient("AzureDevOps");
        client.BaseAddress = new Uri(organizationUrl.TrimEnd('/') + "/");
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{pat}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private AzureDevOpsSourceConfig? ParseSourceConfig(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<AzureDevOpsSourceConfig>(json, JsonOptions); }
        catch (JsonException ex) { _logger.LogWarning(ex, "Failed to deserialize AzureDevOpsSourceConfig from JSON"); return null; }
    }

    private static async Task<T?> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, ct);
    }
}
