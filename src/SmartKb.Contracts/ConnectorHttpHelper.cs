using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace SmartKb.Contracts;

/// <summary>
/// Shared HTTP response deserialization helper for connector clients and webhook managers.
/// Eliminates duplicate private static DeserializeAsync methods across 8 connector files
/// and duplicate ParseSourceConfig methods across 7 connector files.
/// </summary>
public static class ConnectorHttpHelper
{
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
