using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts.Configuration;

namespace SmartKb.Contracts.Services;

/// <summary>
/// Generates embeddings via OpenAI Embeddings API (text-embedding-3-large).
/// Uses HttpClient with the configured API key from application settings (server-side, not Key Vault).
/// </summary>
public sealed class OpenAiEmbeddingService : IEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly OpenAiSettings _openAiSettings;
    private readonly EmbeddingSettings _embeddingSettings;
    private readonly ILogger<OpenAiEmbeddingService> _logger;

    public OpenAiEmbeddingService(
        IHttpClientFactory httpClientFactory,
        OpenAiSettings openAiSettings,
        EmbeddingSettings embeddingSettings,
        ILogger<OpenAiEmbeddingService> logger)
    {
        _httpClient = httpClientFactory.CreateClient("OpenAi");
        _openAiSettings = openAiSettings;
        _embeddingSettings = embeddingSettings;
        _logger = logger;
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text cannot be empty.", nameof(text));

        var requestBody = new
        {
            model = _embeddingSettings.ModelId,
            input = text,
            dimensions = _embeddingSettings.Dimensions,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_openAiSettings.Endpoint}/embeddings");
        request.Headers.Add("Authorization", $"Bearer {_openAiSettings.ApiKey}");
        request.Content = JsonContent.Create(requestBody);

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("OpenAI Embeddings API error {StatusCode}: {Body}", response.StatusCode, errorBody);
            throw new HttpRequestException($"OpenAI Embeddings API returned {response.StatusCode}: {errorBody}");
        }

        var result = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(cancellationToken: cancellationToken);
        if (result?.Data is null || result.Data.Count == 0)
            throw new InvalidOperationException("OpenAI Embeddings API returned no embedding data.");

        return result.Data[0].Embedding;
    }

    internal sealed record EmbeddingResponse
    {
        [JsonPropertyName("data")]
        public List<EmbeddingData> Data { get; init; } = [];
    }

    internal sealed record EmbeddingData
    {
        [JsonPropertyName("embedding")]
        public float[] Embedding { get; init; } = [];
    }
}
