using System.Net;
using System.Text.Json;
using SmartKb.Contracts.Models;
using SmartKb.Eval.Cli;

namespace SmartKb.Eval.Tests;

public class HttpChatOrchestratorClientTests
{
    [Fact]
    public async Task OrchestrateAsync_SendsRequestToCorrectEndpoint()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, MakeApiEnvelope());
        var httpClient = new HttpClient(handler);
        using var client = new HttpChatOrchestratorClient("https://api.example.com", httpClient: httpClient);

        await client.OrchestrateAsync("tenant-1", "user-1", "corr-1",
            new ChatRequest { Query = "test" });

        Assert.Single(handler.Requests);
        Assert.Equal("https://api.example.com/api/chat", handler.Requests[0].RequestUri?.ToString());
    }

    [Fact]
    public async Task OrchestrateAsync_SetsTenantAndCorrelationHeaders()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, MakeApiEnvelope());
        var httpClient = new HttpClient(handler);
        using var client = new HttpChatOrchestratorClient("https://api.example.com", httpClient: httpClient);

        await client.OrchestrateAsync("tenant-abc", "user-1", "corr-xyz",
            new ChatRequest { Query = "test" });

        var request = handler.Requests[0];
        Assert.Equal("tenant-abc", request.Headers.GetValues("X-Tenant-Id").Single());
        Assert.Equal("corr-xyz", request.Headers.GetValues("X-Correlation-Id").Single());
    }

    [Fact]
    public async Task OrchestrateAsync_SetsAuthorizationHeader_WhenTokenProvided()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, MakeApiEnvelope());
        var httpClient = new HttpClient(handler);
        using var client = new HttpChatOrchestratorClient("https://api.example.com", apiToken: "my-token", httpClient: httpClient);

        await client.OrchestrateAsync("tenant-1", "user-1", "corr-1",
            new ChatRequest { Query = "test" });

        var request = handler.Requests[0];
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("my-token", request.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task OrchestrateAsync_ThrowsOnNon2xxStatus()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.InternalServerError, "{}");
        var httpClient = new HttpClient(handler);
        using var client = new HttpChatOrchestratorClient("https://api.example.com", httpClient: httpClient);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.OrchestrateAsync("tenant-1", "user-1", "corr-1",
                new ChatRequest { Query = "test" }));
    }

    [Fact]
    public async Task OrchestrateAsync_ThrowsOnEmptyResponse()
    {
        var envelope = JsonSerializer.Serialize(new { success = true, data = (object?)null },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var handler = new FakeHttpHandler(HttpStatusCode.OK, envelope);
        var httpClient = new HttpClient(handler);
        using var client = new HttpChatOrchestratorClient("https://api.example.com", httpClient: httpClient);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.OrchestrateAsync("tenant-1", "user-1", "corr-1",
                new ChatRequest { Query = "test" }));
    }

    [Fact]
    public void Dispose_WhenHttpClientInjected_DoesNotDisposeIt()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, "{}");
        var httpClient = new HttpClient(handler);

        var client = new HttpChatOrchestratorClient("https://api.example.com", httpClient: httpClient);
        client.Dispose();

        // Injected HttpClient should still be usable — accessing property doesn't throw.
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com");
        var task = httpClient.SendAsync(request);
        Assert.NotNull(task);

        httpClient.Dispose(); // Caller is responsible for disposal.
    }

    [Fact]
    public void Dispose_WhenHttpClientNotInjected_DisposesOwnedClient()
    {
        var client = new HttpChatOrchestratorClient("https://api.example.com");

        // Should not throw — disposes owned HttpClient.
        client.Dispose();
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var client = new HttpChatOrchestratorClient("https://api.example.com");

        client.Dispose();
        client.Dispose(); // GC.SuppressFinalize prevents finalizer; double-dispose is safe.
    }

    [Fact]
    public async Task Constructor_TrimsTrailingSlashFromBaseUrl()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, MakeApiEnvelope());
        var httpClient = new HttpClient(handler);
        using var client = new HttpChatOrchestratorClient("https://api.example.com/", httpClient: httpClient);

        await client.OrchestrateAsync("t", "u", "c", new ChatRequest { Query = "q" });

        Assert.Equal("https://api.example.com/api/chat", handler.Requests[0].RequestUri?.ToString());
    }

    // --- Helpers ---

    private static string MakeApiEnvelope()
    {
        var response = new ChatResponse
        {
            ResponseType = "final_answer",
            Answer = "Test answer",
            Citations = [],
            Confidence = 0.8f,
            ConfidenceLabel = "High",
            HasEvidence = true,
            TraceId = "trace-1",
            SystemPromptVersion = "1.0",
        };

        var envelope = new { success = true, data = response };
        return JsonSerializer.Serialize(envelope, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
    }

    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseBody;
        public List<HttpRequestMessage> Requests { get; } = [];

        public FakeHttpHandler(HttpStatusCode statusCode, string responseBody)
        {
            _statusCode = statusCode;
            _responseBody = responseBody;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody, System.Text.Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }
}
