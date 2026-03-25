using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

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
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
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
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
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
