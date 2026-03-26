using System.Net.Http.Headers;
using System.Net.Mime;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts.Connectors;

namespace SmartKb.Contracts;

/// <summary>
/// Shared helpers for connector clients and webhook managers.
/// Consolidates duplicate DeserializeAsync, ParseSourceConfig, ComputeHash, and CreateHttpClient methods.
/// </summary>
public static class ConnectorHttpHelper
{
    /// <summary>
    /// Configures an HttpClient with Bearer token authentication, base address, and JSON accept header.
    /// Used by HubSpot and ClickUp connector clients and webhook managers.
    /// </summary>
    public static void ConfigureBearerClient(HttpClient client, string baseUrl, string token)
    {
        client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MediaTypeNames.Application.Json));
    }

    /// <summary>
    /// Configures an HttpClient with Basic (PAT) authentication, base address, and JSON accept header.
    /// Used by Azure DevOps connector client and webhook manager.
    /// </summary>
    public static void ConfigureBasicClient(HttpClient client, string baseUrl, string pat)
    {
        client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{pat}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MediaTypeNames.Application.Json));
    }

    /// <summary>
    /// Configures an HttpClient with Bearer token authentication and JSON accept header (no base address).
    /// Used by SharePoint connector client and webhook manager for Microsoft Graph API calls.
    /// </summary>
    public static void ConfigureGraphClient(HttpClient client, string accessToken)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MediaTypeNames.Application.Json));
    }

    /// <summary>
    /// Generates a cryptographically random 32-byte Base64 secret for webhook signatures.
    /// Used by all 4 webhook managers (ADO, HubSpot, ClickUp, SharePoint).
    /// </summary>
    public static string GenerateSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// Computes a lowercase hex SHA-256 hash of the input string.
    /// Used for content deduplication across all connector clients and embedding cache.
    /// </summary>
    public static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes);
    }

    /// <summary>
    /// Deserializes an HTTP response body as <typeparamref name="T"/>.
    /// Returns null on malformed JSON instead of throwing <see cref="JsonException"/>.
    /// </summary>
    public static async Task<T?> DeserializeAsync<T>(
        HttpResponseMessage response,
        JsonSerializerOptions options,
        CancellationToken ct,
        ILogger? logger = null)
    {
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(stream, options, ct);
        }
        catch (JsonException ex)
        {
            logger?.LogWarning(ex, "Failed to deserialize {TypeName} from HTTP response", typeof(T).Name);
            return default;
        }
    }

    /// <summary>
    /// Acquires a Microsoft Graph API access token using the OAuth2 client_credentials grant.
    /// Shared by SharePointConnectorClient and SharePointWebhookManager.
    /// </summary>
    public static async Task<string> AcquireGraphTokenAsync(
        HttpClient httpClient,
        string entraIdTenantId,
        string clientId,
        string clientSecret,
        CancellationToken ct,
        ILogger? logger = null)
    {
        var tokenUrl = string.Format(GraphApiConstants.TokenUrl, entraIdTenantId);
        using var requestBody = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = GraphApiConstants.ClientCredentialsGrantType,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["scope"] = GraphApiConstants.DefaultScope,
        });

        using var response = await httpClient.PostAsync(tokenUrl, requestBody, ct);
        response.EnsureSuccessStatusCode();

        var tokenResponse = await DeserializeAsync<GraphTokenResponse>(response, SharedJsonOptions.CamelCaseIgnoreNull, ct, logger);
        if (tokenResponse?.AccessToken is null)
            throw new InvalidOperationException("Failed to acquire Graph API access token: empty response.");

        return tokenResponse.AccessToken;
    }

    /// <summary>
    /// OAuth2 token response model for Microsoft Graph API client_credentials flow.
    /// </summary>
    internal sealed class GraphTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }

    /// <summary>
    /// Deserializes a JSON string to <typeparamref name="T"/>, returning null on null/empty input or malformed JSON.
    /// Logs a warning on deserialization failure when a logger is provided.
    /// </summary>
    public static T? ParseJson<T>(string? json, JsonSerializerOptions options, ILogger? logger = null) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(json, options);
        }
        catch (JsonException ex)
        {
            logger?.LogWarning(ex, "Failed to deserialize {TypeName} from JSON", typeof(T).Name);
            return null;
        }
    }
}
