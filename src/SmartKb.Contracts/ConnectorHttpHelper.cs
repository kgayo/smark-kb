using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace SmartKb.Contracts;

/// <summary>
/// Shared helpers for connector clients and webhook managers.
/// Consolidates duplicate DeserializeAsync, ParseSourceConfig, and ComputeHash methods.
/// </summary>
public static class ConnectorHttpHelper
{
    /// <summary>
    /// Computes a lowercase hex SHA-256 hash of the input string.
    /// Used for content deduplication across all connector clients and embedding cache.
    /// </summary>
    public static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes);
    }

    public static async Task<T?> DeserializeAsync<T>(
        HttpResponseMessage response,
        JsonSerializerOptions options,
        CancellationToken ct)
    {
        var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync<T>(stream, options, ct);
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
