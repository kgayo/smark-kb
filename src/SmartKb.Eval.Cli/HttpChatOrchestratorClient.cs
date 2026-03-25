using System.Net.Http.Json;
using SmartKb.Contracts;
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
    private readonly string? _apiToken;
    private readonly bool _ownsHttpClient;

    public HttpChatOrchestratorClient(string baseUrl, string? apiToken = null, HttpClient? httpClient = null)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _apiToken = apiToken;
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    }

    public async Task<ChatResponse> OrchestrateAsync(
        string tenantId,
        string userId,
        string correlationId,
        ChatRequest request,
        CancellationToken cancellationToken = default)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/chat")
        {
            Content = JsonContent.Create(request, options: SharedJsonOptions.CamelCase)
        };
        httpRequest.Headers.Add("X-Tenant-Id", tenantId);
        httpRequest.Headers.Add("X-Correlation-Id", correlationId);
        if (!string.IsNullOrEmpty(_apiToken))
            httpRequest.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiToken);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);

        response.EnsureSuccessStatusCode();

        var apiResponse = await response.Content.ReadFromJsonAsync<ApiEnvelope<ChatResponse>>(
            SharedJsonOptions.CamelCase, cancellationToken);

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
