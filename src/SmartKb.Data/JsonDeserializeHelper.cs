using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace SmartKb.Data;

/// <summary>
/// Shared JSON deserialization helpers with structured logging on failure.
/// Replaces ~12 duplicate private Deserialize* methods across Data repositories.
/// </summary>
public static class JsonDeserializeHelper
{
    /// <summary>
    /// Deserializes JSON to <typeparamref name="T"/>, returning <paramref name="fallback"/> on null/empty input or malformed JSON.
    /// </summary>
    public static T Deserialize<T>(string? json, JsonSerializerOptions? options, ILogger logger, T fallback, [System.Runtime.CompilerServices.CallerMemberName] string? caller = null)
    {
        if (string.IsNullOrEmpty(json)) return fallback;
        try
        {
            return JsonSerializer.Deserialize<T>(json, options) ?? fallback;
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to deserialize JSON in {MethodName}", caller);
            return fallback;
        }
    }

    /// <summary>
    /// Deserializes JSON to <typeparamref name="T"/>?, returning null on null/empty input or malformed JSON.
    /// </summary>
    public static T? DeserializeOrNull<T>(string? json, JsonSerializerOptions? options, ILogger logger, [System.Runtime.CompilerServices.CallerMemberName] string? caller = null) where T : class
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(json, options);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to deserialize JSON in {MethodName}", caller);
            return null;
        }
    }
}
