using System.Net.Http.Json;
using System.Text.Json;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;

namespace SmartKb.Eval.Cli;

/// <summary>
/// HTTP-based chat orchestrator client for calling a deployed SmartKB API from CI eval runs.
/// </summary>
public sealed class HttpChatOrchestratorClient : IChatOrchestrator, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly bool _ownsHttpClient;

    public HttpChatOrchestratorClient(string baseUrl, string? apiToken = null, HttpClient? httpClient = null)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

        if (!string.IsNullOrEmpty(apiToken))
            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiToken);
    }

    public async Task<ChatResponse> OrchestrateAsync(
        string tenantId,
        string userId,
        string correlationId,
        ChatRequest request,
        CancellationToken cancellationToken = default)
    {
        _httpClient.DefaultRequestHeaders.Remove("X-Tenant-Id");
        _httpClient.DefaultRequestHeaders.Remove("X-Correlation-Id");
        _httpClient.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId);
        _httpClient.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);

        var response = await _httpClient.PostAsJsonAsync(
            $"{_baseUrl}/api/chat", request, JsonOptions, cancellationToken);

        response.EnsureSuccessStatusCode();

        var apiResponse = await response.Content.ReadFromJsonAsync<ApiEnvelope<ChatResponse>>(
            JsonOptions, cancellationToken);

        return apiResponse?.Data ?? throw new InvalidOperationException(
            $"Empty response from {_baseUrl}/api/chat");
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }

    /// <summary>
    /// Envelope matching the API's ApiResponse&lt;T&gt; shape.
    /// </summary>
    private sealed record ApiEnvelope<T>
    {
        public bool Success { get; init; }
        public T? Data { get; init; }
        public string? Error { get; init; }
        public string? CorrelationId { get; init; }
    }
}
