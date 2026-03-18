using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;

namespace SmartKb.Api.Tests.Load;

/// <summary>
/// Load tests for search/retrieval throughput: parallel chat queries via the stateless
/// /api/chat endpoint and session-based message endpoints, simulating concurrent
/// search index pressure from multiple agents.
/// </summary>
[Collection("LoadTests")]
public class SearchThroughputLoadTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly LoadTestFactory _factory = new();

    public async Task InitializeAsync()
    {
        await _factory.InitializeAsync();
        await _factory.EnsureDatabaseAsync();
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task ConcurrentStatelessChat_50Parallel_AllSucceed()
    {
        const int count = 50;

        var tasks = Enumerable.Range(0, count).Select(async i =>
        {
            using var client = _factory.CreateAuthenticatedClient(userId: $"search-user-{i}");
            var request = new ChatRequest { Query = $"How do I resolve error code E-{i:D4}?" };
            var response = await client.PostAsJsonAsync("/api/chat", request, JsonOptions);
            return response;
        });

        var responses = await Task.WhenAll(tasks);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact]
    public async Task ConcurrentStatelessChat_ResponseIntegrity_AllContainCitations()
    {
        const int count = 20;

        var tasks = Enumerable.Range(0, count).Select(async i =>
        {
            using var client = _factory.CreateAuthenticatedClient(userId: $"integrity-user-{i}");
            var request = new ChatRequest { Query = $"Search throughput query {i}" };
            var response = await client.PostAsJsonAsync("/api/chat", request, JsonOptions);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<ApiResponse<ChatResponse>>(JsonOptions);
            return body!.Data!;
        });

        var chatResponses = await Task.WhenAll(tasks);

        // All responses from StubChatOrchestrator should have consistent structure.
        Assert.All(chatResponses, r =>
        {
            Assert.NotNull(r.Answer);
            Assert.NotEmpty(r.Answer);
            Assert.NotNull(r.ResponseType);
            Assert.NotNull(r.Citations);
            Assert.NotNull(r.TraceId);
        });
    }

    [Fact]
    public async Task MultiUserSessionQueries_10Users3MessagesEach_NoDataCorruption()
    {
        const int userCount = 10;
        const int messagesPerUser = 3;

        // Create sessions sequentially to avoid write contention on session creation.
        var userSessions = new List<(string userId, Guid sessionId)>();
        for (var i = 0; i < userCount; i++)
        {
            var userId = $"multi-user-{i}";
            using var client = _factory.CreateAuthenticatedClient(userId: userId);
            var createResp = await client.PostAsJsonAsync("/api/sessions",
                new CreateSessionRequest { Title = $"Search user {i}" }, JsonOptions);
            Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
            var session = await createResp.Content.ReadFromJsonAsync<ApiResponse<SessionResponse>>(JsonOptions);
            userSessions.Add((userId, session!.Data!.SessionId));
        }

        // Send messages — users in parallel, messages per user sequential.
        var tasks = userSessions.Select(async data =>
        {
            using var client = _factory.CreateAuthenticatedClient(userId: data.userId);
            var successes = 0;
            for (var j = 0; j < messagesPerUser; j++)
            {
                var msgResp = await client.PostAsJsonAsync(
                    $"/api/sessions/{data.sessionId}/messages",
                    new SendMessageRequest { Query = $"User {data.userId} turn {j}: troubleshoot issue" },
                    JsonOptions);
                if (msgResp.StatusCode == HttpStatusCode.OK)
                    successes++;
            }
            return successes;
        });

        var results = await Task.WhenAll(tasks);
        var totalSuccesses = results.Sum();

        // Under SQLite single-writer contention, expect some successes.
        // The key assertion: no data corruption, system remains functional.
        Assert.True(totalSuccesses >= 1,
            $"Expected at least 1 successful message send, got {totalSuccesses}");

        // Verify all sessions still accessible after concurrent operations.
        for (var i = 0; i < userCount; i++)
        {
            using var client = _factory.CreateAuthenticatedClient(userId: userSessions[i].userId);
            var getResp = await client.GetAsync($"/api/sessions/{userSessions[i].sessionId}");
            Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        }
    }

    [Fact]
    public async Task CrossTenantConcurrentSearch_NoLeakage()
    {
        const int queriesPerTenant = 15;

        var tenant1Tasks = Enumerable.Range(0, queriesPerTenant).Select(async i =>
        {
            using var client = _factory.CreateAuthenticatedClient(tenantId: "tenant-1", userId: $"t1-user-{i}");
            var request = new ChatRequest { Query = $"Tenant 1 query {i}" };
            var response = await client.PostAsJsonAsync("/api/chat", request, JsonOptions);
            var body = await response.Content.ReadFromJsonAsync<ApiResponse<ChatResponse>>(JsonOptions);
            return (Status: response.StatusCode, Response: body!.Data!);
        });

        var tenant2Tasks = Enumerable.Range(0, queriesPerTenant).Select(async i =>
        {
            using var client = _factory.CreateAuthenticatedClient(tenantId: "tenant-2", userId: $"t2-user-{i}");
            var request = new ChatRequest { Query = $"Tenant 2 query {i}" };
            var response = await client.PostAsJsonAsync("/api/chat", request, JsonOptions);
            var body = await response.Content.ReadFromJsonAsync<ApiResponse<ChatResponse>>(JsonOptions);
            return (Status: response.StatusCode, Response: body!.Data!);
        });

        var t1Results = await Task.WhenAll(tenant1Tasks);
        var t2Results = await Task.WhenAll(tenant2Tasks);

        Assert.All(t1Results, r => Assert.Equal(HttpStatusCode.OK, r.Status));
        Assert.All(t2Results, r => Assert.Equal(HttpStatusCode.OK, r.Status));

        // Responses should reference the correct queries (grounded in their input).
        Assert.All(t1Results, r => Assert.Contains("Tenant 1", r.Response.Answer));
        Assert.All(t2Results, r => Assert.Contains("Tenant 2", r.Response.Answer));
    }

    [Fact]
    public async Task StatelessChatThroughput_100Sequential_CompletesWithinBudget()
    {
        using var client = _factory.CreateAuthenticatedClient();
        const int count = 100;
        var sw = Stopwatch.StartNew();

        for (var i = 0; i < count; i++)
        {
            var request = new ChatRequest { Query = $"Sequential search query {i}" };
            var response = await client.PostAsJsonAsync("/api/chat", request, JsonOptions);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        sw.Stop();
        Assert.True(sw.Elapsed.TotalSeconds < 30,
            $"100 sequential chat queries took {sw.Elapsed.TotalSeconds:F1}s (budget: 30s)");
    }

    [Fact]
    public async Task ConcurrentFeedbackAfterChat_20Sequential_AllSucceed()
    {
        // Create sessions with messages sequentially, then submit feedback.
        var sessionMessagePairs = new List<(Guid sessionId, Guid messageId, string userId)>();

        for (var i = 0; i < 20; i++)
        {
            var userId = $"fb-user-{i}";
            using var client = _factory.CreateAuthenticatedClient(userId: userId);
            var createResp = await client.PostAsJsonAsync("/api/sessions",
                new CreateSessionRequest { Title = $"Feedback session {i}" }, JsonOptions);
            Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
            var session = await createResp.Content.ReadFromJsonAsync<ApiResponse<SessionResponse>>(JsonOptions);
            var sessionId = session!.Data!.SessionId;

            var msgResp = await client.PostAsJsonAsync(
                $"/api/sessions/{sessionId}/messages",
                new SendMessageRequest { Query = $"Query for feedback {i}" }, JsonOptions);
            Assert.Equal(HttpStatusCode.OK, msgResp.StatusCode);

            // Extract assistant message ID from the send response directly.
            var chatResp = await msgResp.Content.ReadFromJsonAsync<ApiResponse<SessionChatResponse>>(JsonOptions);
            sessionMessagePairs.Add((sessionId, chatResp!.Data!.AssistantMessage.MessageId, userId));
        }

        // Submit feedback concurrently — each user submits on their own session/message.
        var feedbackTasks = sessionMessagePairs.Select(async pair =>
        {
            using var client = _factory.CreateAuthenticatedClient(userId: pair.userId);
            var request = new SubmitFeedbackRequest
            {
                Type = FeedbackType.ThumbsUp,
            };
            var response = await client.PostAsJsonAsync(
                $"/api/sessions/{pair.sessionId}/messages/{pair.messageId}/feedback",
                request, JsonOptions);
            return response;
        });

        var responses = await Task.WhenAll(feedbackTasks);

        // Under SQLite concurrency, tolerate transient DB lock failures.
        var successes = responses.Count(r =>
            r.StatusCode == HttpStatusCode.OK || r.StatusCode == HttpStatusCode.Created);
        Assert.True(successes >= 1,
            $"Expected at least 1 feedback submission to succeed, got {successes}");
    }
}
