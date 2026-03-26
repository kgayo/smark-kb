using System.Text.Json;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts;

namespace SmartKb.Data;

/// <summary>
/// Shared JSON deserialization helpers with structured logging on failure.
/// Replaces ~17 duplicate private Deserialize* methods across Data repositories.
/// </summary>
public static class JsonDeserializeHelper
{
    /// <summary>
    /// Deserializes JSON to <typeparamref name="T"/>, returning <paramref name="fallback"/> on null/empty input or malformed JSON.
    /// </summary>
    public static T Deserialize<T>(string? json, JsonSerializerOptions? options, ILogger? logger, T fallback, [System.Runtime.CompilerServices.CallerMemberName] string? caller = null)
    {
        if (string.IsNullOrEmpty(json)) return fallback;
        try
        {
            return JsonSerializer.Deserialize<T>(json, options) ?? fallback;
        }
        catch (JsonException ex)
        {
            logger?.LogWarning(ex, "Failed to deserialize JSON in {MethodName}", caller);
            return fallback;
        }
    }

    /// <summary>
    /// Deserializes JSON to <typeparamref name="T"/>?, returning null on null/empty input or malformed JSON.
    /// </summary>
    public static T? DeserializeOrNull<T>(string? json, JsonSerializerOptions? options, ILogger? logger, [System.Runtime.CompilerServices.CallerMemberName] string? caller = null) where T : class
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(json, options);
        }
        catch (JsonException ex)
        {
            logger?.LogWarning(ex, "Failed to deserialize JSON in {MethodName}", caller);
            return null;
        }
    }

    /// <summary>
    /// Deserializes a JSON string array, returning an empty list on null/empty/malformed input.
    /// Uses <see cref="SharedJsonOptions.CamelCaseWrite"/> options.
    /// </summary>
    public static IReadOnlyList<string> DeserializeStringList(string? json, ILogger? logger = null, [System.Runtime.CompilerServices.CallerMemberName] string? caller = null) =>
        Deserialize<List<string>>(json, SharedJsonOptions.CamelCaseWrite, logger, [], caller);

    /// <summary>
    /// Deserializes a JSON string array using case-insensitive options, returning an empty list on null/empty/malformed input.
    /// Uses <see cref="SharedJsonOptions.CaseInsensitive"/> options.
    /// </summary>
    public static List<string> DeserializeStringListCaseInsensitive(string? json, ILogger? logger = null, [System.Runtime.CompilerServices.CallerMemberName] string? caller = null) =>
        Deserialize<List<string>>(json, SharedJsonOptions.CaseInsensitive, logger, [], caller);

    /// <summary>
    /// Deserializes a JSON string-to-string dictionary, returning an empty dictionary on null/empty/malformed input.
    /// Uses <see cref="SharedJsonOptions.CamelCaseWrite"/> options.
    /// </summary>
    public static IReadOnlyDictionary<string, string?> DeserializeStringDictionary(string? json, ILogger? logger = null, [System.Runtime.CompilerServices.CallerMemberName] string? caller = null) =>
        Deserialize<Dictionary<string, string?>>(json, SharedJsonOptions.CamelCaseWrite, logger, new Dictionary<string, string?>(), caller);
}
