using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Data;
using SmartKb.Data.Entities;

namespace SmartKb.Api.Tests.Patterns;

public class DistillationEndpointTests : IAsyncLifetime
{
    private readonly DistillationTestFactory _factory = new();
    private HttpClient _adminClient = null!;

    public async Task InitializeAsync()
    {
        await _factory.InitializeAsync();
        _adminClient = _factory.CreateAuthenticatedClient(roles: "Admin");
        await _factory.EnsureDatabaseAsync();
    }

    public async Task DisposeAsync()
    {
        _adminClient.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task GetCandidates_ReturnsEmptyWhenNoCandidates()
    {
        var response = await _adminClient.GetAsync("/api/admin/patterns/candidates");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<DistillationCandidateListResponse>>();
        Assert.True(body!.IsSuccess);
        Assert.Equal(0, body.Data!.TotalCount);
    }

    [Fact]
    public async Task GetCandidates_ReturnsQualifiedSessions()
    {
        await SeedQualifiedSessionAsync();

        var response = await _adminClient.GetAsync("/api/admin/patterns/candidates");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<DistillationCandidateListResponse>>();
        Assert.True(body!.IsSuccess);
        Assert.Equal(1, body.Data!.TotalCount);
        Assert.False(body.Data.Candidates[0].AlreadyDistilled);
    }

    [Fact]
    public async Task Distill_CreatesPatterns()
    {
        await SeedQualifiedSessionAsync();

        var response = await _adminClient.PostAsync("/api/admin/patterns/distill", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<DistillationResult>>();
        Assert.True(body!.IsSuccess);
        Assert.Equal(1, body.Data!.PatternsCreated);
        Assert.Single(body.Data.CreatedPatternIds);
        Assert.Empty(body.Data.Errors);
    }

    [Fact]
    public async Task Distill_ReturnsEmptyWhenNoCandidates()
    {
        var response = await _adminClient.PostAsync("/api/admin/patterns/distill", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<DistillationResult>>();
        Assert.True(body!.IsSuccess);
        Assert.Equal(0, body.Data!.PatternsCreated);
        Assert.Equal(0, body.Data.CandidatesEvaluated);
    }

    [Fact]
    public async Task Endpoints_RequireAuthentication()
    {
        var unauthClient = _factory.CreateClient();

        var getCandidates = await unauthClient.GetAsync("/api/admin/patterns/candidates");
        Assert.Equal(HttpStatusCode.Unauthorized, getCandidates.StatusCode);

        var distill = await unauthClient.PostAsync("/api/admin/patterns/distill", null);
        Assert.Equal(HttpStatusCode.Unauthorized, distill.StatusCode);
    }

    [Fact]
    public async Task Endpoints_RequireAdminRole()
    {
        var agentClient = _factory.CreateAuthenticatedClient(roles: "SupportAgent");

        var getCandidates = await agentClient.GetAsync("/api/admin/patterns/candidates");
        Assert.Equal(HttpStatusCode.Forbidden, getCandidates.StatusCode);

        var distill = await agentClient.PostAsync("/api/admin/patterns/distill", null);
        Assert.Equal(HttpStatusCode.Forbidden, distill.StatusCode);
    }

    [Fact]
    public async Task Distill_RespectsIdempotency_SkipsAlreadyDistilled()
    {
        await SeedQualifiedSessionAsync();

        // First run.
        await _adminClient.PostAsync("/api/admin/patterns/distill", null);

        // Second run — should skip.
        var response = await _adminClient.PostAsync("/api/admin/patterns/distill", null);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<DistillationResult>>();
        Assert.Equal(0, body!.Data!.PatternsCreated);
    }

    private async Task SeedQualifiedSessionAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmartKbDbContext>();

        var connector = new ConnectorEntity
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-1",
            Name = "TestConnector",
            ConnectorType = ConnectorType.AzureDevOps,
            Status = ConnectorStatus.Enabled,
            AuthType = SecretAuthType.Pat,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Connectors.Add(connector);

        var session = new SessionEntity
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-1",
            UserId = "test-user",
            Title = "Auth login issue",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Sessions.Add(session);

        db.OutcomeEvents.Add(new OutcomeEventEntity
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            TenantId = "tenant-1",
            ResolutionType = ResolutionType.ResolvedWithoutEscalation,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var message = new MessageEntity
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            TenantId = "tenant-1",
            Role = MessageRole.Assistant,
            Content = "Here is the fix.",
            CorrelationId = "corr-test-1",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Messages.Add(message);

        var evidenceId = "ev-test-1";
        var chunkId = $"{evidenceId}_chunk_0";

        db.EvidenceChunks.Add(new EvidenceChunkEntity
        {
            ChunkId = chunkId,
            EvidenceId = evidenceId,
            TenantId = "tenant-1",
            ConnectorId = connector.Id,
            ChunkIndex = 0,
            ChunkText = "Resolution: Reset the auth token cache and re-authenticate.",
            SourceSystem = "AzureDevOps",
            SourceType = "Ticket",
            Status = "Closed",
            UpdatedAt = DateTimeOffset.UtcNow,
            Visibility = "Internal",
            AccessLabel = "Internal",
            Title = "Auth token cache fix",
            SourceUrl = "https://dev.azure.com/test/1",
            ContentHash = "hash-test-1",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        db.AnswerTraces.Add(new AnswerTraceEntity
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-1",
            UserId = "test-user",
            CorrelationId = "corr-test-1",
            Query = "How to fix auth?",
            ResponseType = "final_answer",
            Confidence = 0.8f,
            ConfidenceLabel = "High",
            CitedChunkIds = $"[\"{chunkId}\"]",
            RetrievedChunkIds = $"[\"{chunkId}\"]",
            RetrievedChunkCount = 1,
            HasEvidence = true,
            SystemPromptVersion = "1.0",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        db.Feedbacks.Add(new FeedbackEntity
        {
            Id = Guid.NewGuid(),
            MessageId = message.Id,
            SessionId = session.Id,
            TenantId = "tenant-1",
            UserId = "test-user",
            Type = FeedbackType.ThumbsUp,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync();
    }
}
