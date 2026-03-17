using Microsoft.Extensions.Logging.Abstractions;
using SmartKb.Contracts;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data.Entities;
using SmartKb.Data.Repositories;

namespace SmartKb.Data.Tests;

public class EscalationDraftServiceTests : IDisposable
{
    private readonly SmartKbDbContext _db;
    private readonly EscalationDraftService _service;
    private readonly StubAuditWriter _auditWriter;
    private readonly EscalationSettings _settings;

    public EscalationDraftServiceTests()
    {
        _db = TestDbContextFactory.Create();
        _auditWriter = new StubAuditWriter();
        _settings = new EscalationSettings();

        SeedData();

        _service = new EscalationDraftService(
            _db, _auditWriter, _settings, NullLogger<EscalationDraftService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private void SeedData()
    {
        _db.Tenants.Add(new TenantEntity
        {
            TenantId = "t1",
            DisplayName = "Test",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var session = new SessionEntity
        {
            Id = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"),
            TenantId = "t1",
            UserId = "u1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.Sessions.Add(session);

        var message = new MessageEntity
        {
            Id = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001"),
            SessionId = session.Id,
            TenantId = "t1",
            Role = SmartKb.Contracts.Enums.MessageRole.Assistant,
            Content = "Escalation recommended.",
            ResponseType = "escalate",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.Messages.Add(message);
        _db.SaveChanges();
    }

    private static readonly Guid SessionId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid MessageId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");

    private CreateEscalationDraftRequest MakeRequest(string? title = null) => new()
    {
        SessionId = SessionId,
        MessageId = MessageId,
        Title = title ?? "Test Escalation",
        CustomerSummary = "Customer cannot log in.",
        StepsToReproduce = "1. Go to login. 2. Enter credentials. 3. Error shown.",
        LogsIdsRequested = "correlation-id-123",
        SuspectedComponent = "Auth Service",
        Severity = "P2",
        EvidenceLinks = [new CitationDto
        {
            ChunkId = "chunk_0",
            EvidenceId = "ev-1",
            Title = "Login Error",
            SourceUrl = "https://example.com/wiki/login",
            SourceSystem = "Wiki",
            Snippet = "Login failures observed.",
            UpdatedAt = DateTimeOffset.UtcNow,
            AccessLabel = "Internal",
        }],
        TargetTeam = "Auth Team",
        Reason = "Repeated login failures affecting multiple customers.",
    };

    [Fact]
    public async Task CreateDraft_Succeeds_WithValidData()
    {
        var result = await _service.CreateDraftAsync("t1", "u1", "corr-1", MakeRequest());

        Assert.NotEqual(Guid.Empty, result.DraftId);
        Assert.Equal(SessionId, result.SessionId);
        Assert.Equal(MessageId, result.MessageId);
        Assert.Equal("Test Escalation", result.Title);
        Assert.Equal("P2", result.Severity);
        Assert.Equal("Auth Team", result.TargetTeam);
        Assert.Single(result.EvidenceLinks);
        Assert.Equal("chunk_0", result.EvidenceLinks[0].ChunkId);
    }

    [Fact]
    public async Task CreateDraft_WritesAuditEvent()
    {
        await _service.CreateDraftAsync("t1", "u1", "corr-1", MakeRequest());
        Assert.Single(_auditWriter.Events);
        Assert.Equal(AuditEventTypes.EscalationDraftCreated, _auditWriter.Events[0].EventType);
    }

    [Fact]
    public async Task CreateDraft_Throws_WhenSessionNotOwned()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateDraftAsync("t1", "other-user", "corr-1", MakeRequest()));
    }

    [Fact]
    public async Task CreateDraft_Throws_WhenMessageNotInSession()
    {
        var request = MakeRequest() with { MessageId = Guid.NewGuid() };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateDraftAsync("t1", "u1", "corr-1", request));
    }

    [Fact]
    public async Task CreateDraft_DefaultsTitle_WhenEmpty()
    {
        var request = MakeRequest() with { Title = "" };
        var result = await _service.CreateDraftAsync("t1", "u1", "corr-1", request);
        Assert.Equal("Escalation Draft", result.Title);
    }

    [Fact]
    public async Task CreateDraft_NormalizesSeverity()
    {
        var request = MakeRequest() with { Severity = "p1" };
        var result = await _service.CreateDraftAsync("t1", "u1", "corr-1", request);
        Assert.Equal("P1", result.Severity);
    }

    [Fact]
    public async Task CreateDraft_FallsBackSeverity_WhenInvalid()
    {
        var request = MakeRequest() with { Severity = "Critical" };
        var result = await _service.CreateDraftAsync("t1", "u1", "corr-1", request);
        Assert.Equal("P3", result.Severity);
    }

    [Fact]
    public async Task CreateDraft_UsesRoutingRule_WhenTargetTeamEmpty()
    {
        _db.EscalationRoutingRules.Add(new EscalationRoutingRuleEntity
        {
            Id = Guid.NewGuid(),
            TenantId = "t1",
            ProductArea = "Auth Service",
            TargetTeam = "Identity Team",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        var request = MakeRequest() with { TargetTeam = "" };
        var result = await _service.CreateDraftAsync("t1", "u1", "corr-1", request);
        Assert.Equal("Identity Team", result.TargetTeam);
    }

    [Fact]
    public async Task CreateDraft_UsesFallbackTeam_WhenNoRule()
    {
        var request = MakeRequest() with { TargetTeam = "", SuspectedComponent = "Unknown" };
        var result = await _service.CreateDraftAsync("t1", "u1", "corr-1", request);
        Assert.Equal("Engineering", result.TargetTeam);
    }

    [Fact]
    public async Task GetDraft_ReturnsDraft()
    {
        var created = await _service.CreateDraftAsync("t1", "u1", "corr-1", MakeRequest());
        var result = await _service.GetDraftAsync("t1", "u1", created.DraftId);
        Assert.NotNull(result);
        Assert.Equal(created.DraftId, result!.DraftId);
    }

    [Fact]
    public async Task GetDraft_ReturnsNull_WhenWrongUser()
    {
        var created = await _service.CreateDraftAsync("t1", "u1", "corr-1", MakeRequest());
        var result = await _service.GetDraftAsync("t1", "other-user", created.DraftId);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetDraft_ReturnsNull_WhenWrongTenant()
    {
        var created = await _service.CreateDraftAsync("t1", "u1", "corr-1", MakeRequest());
        var result = await _service.GetDraftAsync("other-tenant", "u1", created.DraftId);
        Assert.Null(result);
    }

    [Fact]
    public async Task ListDrafts_ReturnsSessionDrafts()
    {
        await _service.CreateDraftAsync("t1", "u1", "corr-1", MakeRequest("Draft A"));
        await _service.CreateDraftAsync("t1", "u1", "corr-2", MakeRequest("Draft B"));

        var result = await _service.ListDraftsAsync("t1", "u1", SessionId);
        Assert.NotNull(result);
        Assert.Equal(2, result!.TotalCount);
        Assert.Equal(SessionId, result.SessionId);
    }

    [Fact]
    public async Task ListDrafts_ReturnsNull_WhenSessionNotFound()
    {
        var result = await _service.ListDraftsAsync("t1", "u1", Guid.NewGuid());
        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateDraft_UpdatesFields()
    {
        var created = await _service.CreateDraftAsync("t1", "u1", "corr-1", MakeRequest());
        var update = new UpdateEscalationDraftRequest
        {
            Title = "Updated Title",
            Severity = "P1",
            TargetTeam = "Infra Team",
        };

        var (result, notFound) = await _service.UpdateDraftAsync("t1", "u1", created.DraftId, update);
        Assert.False(notFound);
        Assert.NotNull(result);
        Assert.Equal("Updated Title", result!.Title);
        Assert.Equal("P1", result.Severity);
        Assert.Equal("Infra Team", result.TargetTeam);
        // Unchanged fields preserved.
        Assert.Equal("Customer cannot log in.", result.CustomerSummary);
    }

    [Fact]
    public async Task UpdateDraft_ReturnsNotFound_WhenMissing()
    {
        var (result, notFound) = await _service.UpdateDraftAsync("t1", "u1", Guid.NewGuid(),
            new UpdateEscalationDraftRequest { Title = "x" });
        Assert.True(notFound);
        Assert.Null(result);
    }

    [Fact]
    public async Task ExportDraft_ReturnsMarkdown()
    {
        var created = await _service.CreateDraftAsync("t1", "u1", "corr-1", MakeRequest());
        var result = await _service.ExportDraftAsMarkdownAsync("t1", "u1", created.DraftId);

        Assert.NotNull(result);
        Assert.Equal(created.DraftId, result!.DraftId);
        Assert.Contains("# Test Escalation", result.Markdown);
        Assert.Contains("**Severity:** P2", result.Markdown);
        Assert.Contains("**Target Team:** Auth Team", result.Markdown);
        Assert.Contains("## Customer Summary", result.Markdown);
        Assert.Contains("## Steps to Reproduce", result.Markdown);
        Assert.Contains("## Evidence Links", result.Markdown);
        Assert.Contains("Login Error", result.Markdown);
    }

    [Fact]
    public async Task ExportDraft_SetsExportedAt()
    {
        var created = await _service.CreateDraftAsync("t1", "u1", "corr-1", MakeRequest());
        Assert.Null(created.ExportedAt);

        var exported = await _service.ExportDraftAsMarkdownAsync("t1", "u1", created.DraftId);
        Assert.NotEqual(default, exported!.ExportedAt);

        // Verify exported timestamp persisted.
        var fetched = await _service.GetDraftAsync("t1", "u1", created.DraftId);
        Assert.NotNull(fetched!.ExportedAt);
    }

    [Fact]
    public async Task ExportDraft_ReturnsNull_WhenNotFound()
    {
        var result = await _service.ExportDraftAsMarkdownAsync("t1", "u1", Guid.NewGuid());
        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteDraft_SoftDeletes()
    {
        var created = await _service.CreateDraftAsync("t1", "u1", "corr-1", MakeRequest());
        var deleted = await _service.DeleteDraftAsync("t1", "u1", created.DraftId);
        Assert.True(deleted);

        // Should no longer be visible (query filter).
        var after = await _service.GetDraftAsync("t1", "u1", created.DraftId);
        Assert.Null(after);
    }

    [Fact]
    public async Task DeleteDraft_ReturnsFalse_WhenNotFound()
    {
        var result = await _service.DeleteDraftAsync("t1", "u1", Guid.NewGuid());
        Assert.False(result);
    }

    private sealed class StubAuditWriter : IAuditEventWriter
    {
        public List<AuditEvent> Events { get; } = [];

        public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }
    }
}
