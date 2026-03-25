using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace SmartKb.Contracts.Services;

/// <summary>
/// Shared helper for OpenAI Chat Completions response parsing and request setup.
/// Consolidates the choices[0].message.content extraction pattern duplicated across
/// ChatOrchestrator, OpenAiQueryClassificationService, and OpenAiSessionSummarizationService.
/// Also centralizes the Authorization header setup duplicated across all 4 OpenAI services.
/// </summary>
public static class OpenAiResponseHelper
{
    /// <summary>
    /// Extracts and deserializes the structured content from an OpenAI Chat Completions response.
    /// Navigates choices[0].message.content, deserializes to <typeparamref name="T"/>,
    /// and gracefully handles malformed JSON with a logged fallback.
    /// </summary>
    /// <typeparam name="T">The target type to deserialize the content into.</typeparam>
    /// <param name="responseJson">The parsed JSON response from the OpenAI API.</param>
    /// <param name="options">JSON serializer options to use for deserialization.</param>
    /// <param name="logger">Optional logger for warning on deserialization failures.</param>
    /// <returns>The deserialized result, or null if extraction or deserialization fails.</returns>
    public static T? ExtractContent<T>(JsonElement responseJson, JsonSerializerOptions options, ILogger? logger = null)
        where T : class
    {
        if (responseJson.TryGetProperty("choices", out var choices) &&
            choices.GetArrayLength() > 0 &&
            choices[0].TryGetProperty("message", out var message) &&
            message.TryGetProperty("content", out var content))
        {
            var messageContent = content.GetString();

            if (!string.IsNullOrEmpty(messageContent))
            {
                try
                {
                    return JsonSerializer.Deserialize<T>(messageContent, options);
                }
                catch (JsonException ex)
                {
                    logger?.LogWarning(ex,
                        "Failed to deserialize OpenAI response content to {TargetType}.",
                        typeof(T).Name);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts token usage (prompt_tokens, completion_tokens, total_tokens) from an OpenAI response.
    /// </summary>
    public static (int PromptTokens, int CompletionTokens, int TotalTokens) ExtractTokenUsage(JsonElement responseJson)
    {
        int promptTokens = 0, completionTokens = 0, totalTokens = 0;

        if (responseJson.TryGetProperty("usage", out var usage))
        {
            if (usage.TryGetProperty("prompt_tokens", out var pt)) promptTokens = pt.GetInt32();
            if (usage.TryGetProperty("completion_tokens", out var ct)) completionTokens = ct.GetInt32();
            if (usage.TryGetProperty("total_tokens", out var tt)) totalTokens = tt.GetInt32();
        }

        return (promptTokens, completionTokens, totalTokens);
    }

    /// <summary>
    /// Adds the OpenAI Bearer authorization header to an HTTP request message.
    /// </summary>
    public static void AddAuthorizationHeader(HttpRequestMessage request, string apiKey)
    {
        request.Headers.Add("Authorization", $"Bearer {apiKey}");
    }
}
