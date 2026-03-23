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
/// Manages Microsoft Graph change notification subscriptions for SharePoint drive items.
/// Registers subscriptions per drive with clientState secret for signature verification.
/// Graph subscriptions expire (max ~4230 minutes for drive items). The polling fallback
/// service handles missed notifications when subscriptions expire or fail.
/// </summary>
public sealed class SharePointWebhookManager : IWebhookManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private const string GraphBaseUrl = "https://graph.microsoft.com/v1.0";
    private const string GraphTokenUrl = "https://login.microsoftonline.com/{0}/oauth2/v2.0/token";

    // Graph allows max 4230 minutes (about 2.94 days) for driveItem subscriptions.
    private static readonly TimeSpan SubscriptionLifetime = TimeSpan.FromMinutes(4230);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ITextExtractionService _textExtractor;
    private readonly ILogger<SharePointWebhookManager> _logger;

    public SharePointWebhookManager(
        IHttpClientFactory httpClientFactory,
        ITextExtractionService textExtractor,
        ILogger<SharePointWebhookManager> logger)
    {
        _httpClientFactory = httpClientFactory;
        _textExtractor = textExtractor;
        _logger = logger;
    }

    public ConnectorType Type => ConnectorType.SharePoint;

    public async Task<IReadOnlyList<WebhookRegistrationResult>> RegisterAsync(
        WebhookRegistrationContext context,
        CancellationToken cancellationToken = default)
    {
        var config = SharePointConnectorClient.ParseSourceConfig(context.SourceConfig, _logger);
        if (config is null)
        {
            _logger.LogWarning("Cannot register SharePoint webhooks: invalid source config for connector {ConnectorId}", context.ConnectorId);
            return [];
        }

        if (string.IsNullOrEmpty(context.SecretValue))
        {
            _logger.LogWarning("Cannot register SharePoint webhooks: no credentials for connector {ConnectorId}", context.ConnectorId);
            return [];
        }

        string accessToken;
        try
        {
            accessToken = await AcquireTokenAsync(config, context.SecretValue, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to acquire token for SharePoint webhook registration (connector={ConnectorId})", context.ConnectorId);
            return [];
        }

        using var graphClient = CreateGraphClient(accessToken);

        // Resolve site and drives.
        var spClient = new SharePointConnectorClient(_httpClientFactory, _textExtractor, _logger as ILogger<SharePointConnectorClient> ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<SharePointConnectorClient>.Instance);
        var siteId = await spClient.ResolveSiteIdAsync(graphClient, config.SiteUrl, cancellationToken);
        if (siteId is null)
        {
            _logger.LogWarning("Cannot register SharePoint webhooks: could not resolve site for connector {ConnectorId}", context.ConnectorId);
            return [];
        }

        var drives = await spClient.ResolveDrivesAsync(graphClient, siteId, config, cancellationToken);
        if (drives.Count == 0)
        {
            _logger.LogWarning("No drives found for SharePoint webhook registration (connector={ConnectorId})", context.ConnectorId);
            return [];
        }

        var results = new List<WebhookRegistrationResult>();
        var clientState = GenerateClientState();
        var callbackUrl = $"{context.CallbackBaseUrl.TrimEnd('/')}/api/webhooks/msgraph/{context.ConnectorId}";

        foreach (var drive in drives)
        {
            try
            {
                var subscriptionRequest = new GraphSubscriptionRequest
                {
                    ChangeType = "created,updated,deleted",
                    NotificationUrl = callbackUrl,
                    Resource = $"/drives/{drive.Id}/root",
                    ExpirationDateTime = DateTimeOffset.UtcNow.Add(SubscriptionLifetime),
                    ClientState = clientState,
                };

                var json = JsonSerializer.Serialize(subscriptionRequest, JsonOptions);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await graphClient.PostAsync($"{GraphBaseUrl}/subscriptions", content, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var result = await DeserializeAsync<GraphSubscriptionResponse>(response, cancellationToken);
                    if (result?.Id is not null)
                    {
                        results.Add(new WebhookRegistrationResult(
                            ExternalSubscriptionId: result.Id,
                            EventType: $"driveItem.changed.{drive.Id}",
                            CallbackUrl: callbackUrl,
                            WebhookSecret: clientState));

                        _logger.LogInformation(
                            "Registered Graph subscription: connector={ConnectorId}, drive={DriveId}, subscriptionId={SubscriptionId}, expires={Expiry}",
                            context.ConnectorId, drive.Id, result.Id, result.ExpirationDateTime);
                    }
                }
                else
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogWarning(
                        "Failed to register Graph subscription: connector={ConnectorId}, drive={DriveId}, status={Status}, body={Body}",
                        context.ConnectorId, drive.Id, (int)response.StatusCode,
                        body.Length > 500 ? body[..500] : body);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _logger.LogWarning(ex,
                    "Error registering Graph subscription: connector={ConnectorId}, drive={DriveId}",
                    context.ConnectorId, drive.Id);
            }
        }

        return results;
    }

    public async Task DeregisterAsync(
        WebhookDeregistrationContext context,
        CancellationToken cancellationToken = default)
    {
        var config = SharePointConnectorClient.ParseSourceConfig(context.SourceConfig, _logger);
        if (config is null || string.IsNullOrEmpty(context.SecretValue))
        {
            _logger.LogWarning("Cannot deregister SharePoint webhooks: invalid config or credentials for connector {ConnectorId}",
                context.ConnectorId);
            return;
        }

        string accessToken;
        try
        {
            accessToken = await AcquireTokenAsync(config, context.SecretValue, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to acquire token for SharePoint webhook deregistration (connector={ConnectorId})", context.ConnectorId);
            return;
        }

        using var graphClient = CreateGraphClient(accessToken);

        foreach (var subscriptionId in context.ExternalSubscriptionIds)
        {
            try
            {
                var url = $"{GraphBaseUrl}/subscriptions/{subscriptionId}";
                var response = await graphClient.DeleteAsync(url, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation(
                        "Deregistered Graph subscription: connector={ConnectorId}, subscriptionId={SubscriptionId}",
                        context.ConnectorId, subscriptionId);
                }
                else
                {
                    _logger.LogWarning(
                        "Failed to deregister Graph subscription: connector={ConnectorId}, subscriptionId={SubscriptionId}, status={Status}",
                        context.ConnectorId, subscriptionId, (int)response.StatusCode);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _logger.LogWarning(ex,
                    "Error deregistering Graph subscription: connector={ConnectorId}, subscriptionId={SubscriptionId}",
                    context.ConnectorId, subscriptionId);
            }
        }
    }

    internal static string GenerateClientState()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes);
    }

    private async Task<string> AcquireTokenAsync(
        SharePointSourceConfig config, string clientSecret, CancellationToken ct)
    {
        using var client = _httpClientFactory.CreateClient("SharePoint");
        var tokenUrl = string.Format(GraphTokenUrl, config.EntraIdTenantId);
        var requestBody = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = config.ClientId,
            ["client_secret"] = clientSecret,
            ["scope"] = "https://graph.microsoft.com/.default",
        });

        var response = await client.PostAsync(tokenUrl, requestBody, ct);
        response.EnsureSuccessStatusCode();

        var tokenResponse = await DeserializeAsync<SharePointConnectorClient.OAuthTokenResponse>(response, ct);
        return tokenResponse?.AccessToken
            ?? throw new InvalidOperationException("Failed to acquire access token.");
    }

    private HttpClient CreateGraphClient(string accessToken)
    {
        var client = _httpClientFactory.CreateClient("SharePoint");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static async Task<T?> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, ct);
    }
}
