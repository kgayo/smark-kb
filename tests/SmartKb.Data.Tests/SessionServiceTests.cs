using Microsoft.Extensions.Logging.Abstractions;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data.Entities;
using SmartKb.Data.Repositories;

namespace SmartKb.Data.Tests;

public class SessionServiceTests : IDisposable
{
    private readonly SmartKbDbContext _db;
    private readonly SessionService _service;
    private readonly SessionSettings _settings;

    public SessionServiceTests()
    {
        _db = TestDbContextFactory.Create();
        _settings = new SessionSettings { DefaultExpiryHours = 24, MaxMessagesPerSession = 10 };

        // Seed tenant.
        _db.Tenants.Add(new TenantEntity
        {
            TenantId = "t1",
            DisplayName = "Test",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();

        _service = new SessionService(
            _db, _settings, NullLogger<SessionService>.Instance, new StubOrchestrator());
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task CreateSession_SetsExpiresAt()
    {
        var result = await _service.CreateSessionAsync("t1", "u1", new CreateSessionRequest { Title = "Test" });
        Assert.NotNull(result.ExpiresAt);
        Assert.True(result.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task CreateSession_NoExpiry_WhenZeroHours()
    {
        _settings.DefaultExpiryHours = 0;
        var svc = new SessionService(_db, _settings, NullLogger<SessionService>.Instance);
        var result = await svc.CreateSessionAsync("t1", "u1", new CreateSessionRequest());
        Assert.Null(result.ExpiresAt);
    }

    [Fact]
    public async Task ListSessions_ReturnsOnlyUsersSessions()
    {
        await _service.CreateSessionAsync("t1", "u1", new CreateSessionRequest { Title = "A" });
        await _service.CreateSessionAsync("t1", "u2", new CreateSessionRequest { Title = "B" });

        var result = await _service.ListSessionsAsync("t1", "u1");
        Assert.Single(result.Sessions);
        Assert.Equal("A", result.Sessions[0].Title);
    }

    [Fact]
    public async Task GetSession_ReturnsNull_WhenWrongTenant()
    {
        var created = await _service.CreateSessionAsync("t1", "u1", new CreateSessionRequest());
        var result = await _service.GetSessionAsync("other-tenant", "u1", created.SessionId);
        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteSession_SoftDeletes()
    {
        var created = await _service.CreateSessionAsync("t1", "u1", new CreateSessionRequest());
        var deleted = await _service.DeleteSessionAsync("t1", "u1", created.SessionId);
        Assert.True(deleted);

        var after = await _service.GetSessionAsync("t1", "u1", created.SessionId);
        Assert.Null(after);
    }

    [Fact]
    public async Task DeleteSession_ReturnsFalse_WhenNotFound()
    {
        var result = await _service.DeleteSessionAsync("t1", "u1", Guid.NewGuid());
        Assert.False(result);
    }

    [Fact]
    public async Task SendMessage_PersistsBothMessages()
    {
        var session = await _service.CreateSessionAsync("t1", "u1", new CreateSessionRequest());
        var result = await _service.SendMessageAsync("t1", "u1", "corr-1", session.SessionId,
            new SendMessageRequest { Query = "test query" });

        Assert.NotNull(result);
        Assert.Equal("User", result!.UserMessage.Role);
        Assert.Equal("test query", result.UserMessage.Content);
        Assert.Equal("Assistant", result.AssistantMessage.Role);
        Assert.Equal(2, result.Session.MessageCount);
    }

    [Fact]
    public async Task SendMessage_ReturnsNull_ForNonexistentSession()
    {
        var result = await _service.SendMessageAsync("t1", "u1", "corr-1", Guid.NewGuid(),
            new SendMessageRequest { Query = "test" });
        Assert.Null(result);
    }

    [Fact]
    public async Task SendMessage_AutoTitles_WhenNoTitle()
    {
        var session = await _service.CreateSessionAsync("t1", "u1", new CreateSessionRequest());
        Assert.Null(session.Title);

        var result = await _service.SendMessageAsync("t1", "u1", "corr-1", session.SessionId,
            new SendMessageRequest { Query = "My question" });

        Assert.Equal("My question", result!.Session.Title);
    }

    [Fact]
    public async Task SendMessage_PreservesExistingTitle()
    {
        var session = await _service.CreateSessionAsync("t1", "u1", new CreateSessionRequest { Title = "Existing" });
        var result = await _service.SendMessageAsync("t1", "u1", "corr-1", session.SessionId,
            new SendMessageRequest { Query = "My question" });

        Assert.Equal("Existing", result!.Session.Title);
    }

    [Fact]
    public async Task SendMessage_PersistsCitations_AsJson()
    {
        var session = await _service.CreateSessionAsync("t1", "u1", new CreateSessionRequest());
        await _service.SendMessageAsync("t1", "u1", "corr-1", session.SessionId,
            new SendMessageRequest { Query = "test" });

        var messages = await _service.GetMessagesAsync("t1", "u1", session.SessionId);
        var assistant = messages!.Messages.First(m => m.Role == "Assistant");
        Assert.NotNull(assistant.Citations);
        Assert.Single(assistant.Citations!);
        Assert.Equal("stub_chunk_0", assistant.Citations![0].ChunkId);
    }

    [Fact]
    public async Task SendMessage_FollowUp_IncludesSessionHistory()
    {
        var session = await _service.CreateSessionAsync("t1", "u1", new CreateSessionRequest());

        await _service.SendMessageAsync("t1", "u1", "corr-1", session.SessionId,
            new SendMessageRequest { Query = "first" });
        var result = await _service.SendMessageAsync("t1", "u1", "corr-2", session.SessionId,
            new SendMessageRequest { Query = "follow-up" });

        Assert.NotNull(result);
        Assert.Equal(4, result!.Session.MessageCount);
    }

    [Fact]
    public async Task GetMessages_ReturnsChronologicalOrder()
    {
        var session = await _service.CreateSessionAsync("t1", "u1", new CreateSessionRequest());

        await _service.SendMessageAsync("t1", "u1", "corr-1", session.SessionId,
            new SendMessageRequest { Query = "first" });
        await _service.SendMessageAsync("t1", "u1", "corr-2", session.SessionId,
            new SendMessageRequest { Query = "second" });

        var messages = await _service.GetMessagesAsync("t1", "u1", session.SessionId);
        Assert.Equal(4, messages!.TotalCount);
        Assert.Equal("User", messages.Messages[0].Role);
        Assert.Equal("first", messages.Messages[0].Content);
        Assert.Equal("Assistant", messages.Messages[1].Role);
        Assert.Equal("User", messages.Messages[2].Role);
        Assert.Equal("second", messages.Messages[2].Content);
    }

    [Fact]
    public async Task SendMessage_ExtendsExpiry()
    {
        var session = await _service.CreateSessionAsync("t1", "u1", new CreateSessionRequest());
        var originalExpiry = session.ExpiresAt;
        Assert.NotNull(originalExpiry);

        // Small delay to ensure different timestamp.
        await Task.Delay(10);

        var result = await _service.SendMessageAsync("t1", "u1", "corr-1", session.SessionId,
            new SendMessageRequest { Query = "test" });

        Assert.True(result!.Session.ExpiresAt >= originalExpiry);
    }

    private sealed class StubOrchestrator : IChatOrchestrator
    {
        public Task<ChatResponse> OrchestrateAsync(
            string tenantId, string userId, string correlationId,
            ChatRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ChatResponse
            {
                ResponseType = "final_answer",
                Answer = $"Answer for: {request.Query}",
                Citations =
                [
                    new CitationDto
                    {
                        ChunkId = "stub_chunk_0",
                        EvidenceId = "stub-ev-1",
                        Title = "Stub Article",
                        SourceUrl = "https://example.com/stub",
                        SourceSystem = "Test",
                        Snippet = "Stub snippet",
                        UpdatedAt = DateTimeOffset.UtcNow,
                        AccessLabel = "Internal",
                    }
                ],
                Confidence = 0.85f,
                ConfidenceLabel = "High",
                NextSteps = ["Check docs."],
                TraceId = correlationId,
                HasEvidence = true,
                SystemPromptVersion = "1.0",
            });
        }
    }
}
