using Microsoft.Extensions.Logging.Abstractions;
using SmartKb.Contracts;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data.Entities;
using SmartKb.Data.Repositories;

namespace SmartKb.Data.Tests;

public class DataSubjectDeletionServiceTests : IDisposable
{
    private readonly SmartKbDbContext _db;
    private readonly StubAuditWriter _auditWriter;
    private readonly DataSubjectDeletionService _service;

    public DataSubjectDeletionServiceTests()
    {
        _db = TestDbContextFactory.Create();
        _auditWriter = new StubAuditWriter();

        _db.Tenants.Add(new TenantEntity
        {
            TenantId = "t1",
            DisplayName = "Test Tenant",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();

        _service = new DataSubjectDeletionService(
            _db, _auditWriter, NullLogger<DataSubjectDeletionService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task RequestDeletion_NoData_CompletesWithZeroCounts()
    {
        var result = await _service.RequestDeletionAsync("t1", "user-1", "admin-1");

        Assert.Equal("Completed", result.Status);
        Assert.Equal("user-1", result.SubjectId);
        Assert.NotNull(result.DeletionSummary);
        Assert.Equal(0, result.DeletionSummary!.Values.Sum());
    }

    [Fact]
    public async Task RequestDeletion_DeletesSessions_Messages_Feedbacks_Traces()
    {
        // Seed data for user-1.
        var sessionId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        _db.Sessions.Add(new SessionEntity
        {
            Id = sessionId, TenantId = "t1", UserId = "user-1", Title = "User Session",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.Messages.Add(new MessageEntity
        {
            Id = messageId, TenantId = "t1", SessionId = sessionId,
            Role = SmartKb.Contracts.Enums.MessageRole.User, Content = "Hello",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.Feedbacks.Add(new FeedbackEntity
        {
            Id = Guid.NewGuid(), TenantId = "t1", UserId = "user-1",
            SessionId = sessionId, MessageId = messageId,
            Type = SmartKb.Contracts.Enums.FeedbackType.ThumbsUp,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.AnswerTraces.Add(new AnswerTraceEntity
        {
            Id = Guid.NewGuid(), TenantId = "t1", UserId = "user-1",
            CorrelationId = "c1", Query = "test", ResponseType = "final_answer",
            ConfidenceLabel = "High", CitedChunkIds = "[]", RetrievedChunkIds = "[]",
            SystemPromptVersion = "v1", CreatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        var result = await _service.RequestDeletionAsync("t1", "user-1", "admin-1");

        Assert.Equal("Completed", result.Status);
        Assert.NotNull(result.DeletionSummary);
        Assert.Equal(1, result.DeletionSummary!["sessions"]);
        Assert.Equal(1, result.DeletionSummary!["messages"]);
        Assert.Equal(1, result.DeletionSummary!["feedbacks"]);
        Assert.Equal(1, result.DeletionSummary!["answer_traces"]);
    }

    [Fact]
    public async Task RequestDeletion_DoesNotAffectOtherUsers()
    {
        var s1 = Guid.NewGuid();
        var s2 = Guid.NewGuid();
        _db.Sessions.Add(new SessionEntity
        {
            Id = s1, TenantId = "t1", UserId = "user-1", Title = "U1 Session",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.Sessions.Add(new SessionEntity
        {
            Id = s2, TenantId = "t1", UserId = "user-2", Title = "U2 Session",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.Messages.Add(new MessageEntity
        {
            Id = Guid.NewGuid(), TenantId = "t1", SessionId = s2,
            Role = SmartKb.Contracts.Enums.MessageRole.User, Content = "Preserved",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        await _service.RequestDeletionAsync("t1", "user-1", "admin-1");

        // user-2 data should remain.
        Assert.Equal(1, _db.Sessions.Count());
        Assert.Equal(1, _db.Messages.Count());
    }

    [Fact]
    public async Task RequestDeletion_EmitsAuditEvents()
    {
        await _service.RequestDeletionAsync("t1", "user-1", "admin-1");

        Assert.Contains(_auditWriter.Events,
            e => e.EventType == AuditEventTypes.DataSubjectDeletionRequested);
        Assert.Contains(_auditWriter.Events,
            e => e.EventType == AuditEventTypes.DataSubjectDeletionCompleted);
    }

    [Fact]
    public async Task GetDeletionRequest_Existing_ReturnsResponse()
    {
        var created = await _service.RequestDeletionAsync("t1", "user-1", "admin-1");

        var result = await _service.GetDeletionRequestAsync("t1", created.RequestId);

        Assert.NotNull(result);
        Assert.Equal(created.RequestId, result.RequestId);
        Assert.Equal("user-1", result.SubjectId);
        Assert.Equal("Completed", result.Status);
    }

    [Fact]
    public async Task GetDeletionRequest_NonExistent_ReturnsNull()
    {
        var result = await _service.GetDeletionRequestAsync("t1", Guid.NewGuid());
        Assert.Null(result);
    }

    [Fact]
    public async Task ListDeletionRequests_ReturnsAll()
    {
        await _service.RequestDeletionAsync("t1", "user-1", "admin-1");
        await _service.RequestDeletionAsync("t1", "user-2", "admin-1");

        var result = await _service.ListDeletionRequestsAsync("t1");

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.Requests.Count);
    }

    [Fact]
    public async Task RequestDeletion_SoftDeletesEscalationDrafts()
    {
        var sessionId = Guid.NewGuid();
        _db.Sessions.Add(new SessionEntity
        {
            Id = sessionId, TenantId = "t1", UserId = "user-1", Title = "Session",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.EscalationDrafts.Add(new EscalationDraftEntity
        {
            Id = Guid.NewGuid(), TenantId = "t1", UserId = "user-1",
            SessionId = sessionId, Title = "Escalation Draft",
            CustomerSummary = "summary", StepsToReproduce = "steps",
            LogsIdsRequested = "logs", SuspectedComponent = "comp",
            Severity = "P1", EvidenceLinksJson = "[]", TargetTeam = "team",
            Reason = "reason", CreatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        var result = await _service.RequestDeletionAsync("t1", "user-1", "admin-1");

        Assert.Equal(1, result.DeletionSummary!["escalation_drafts"]);
        Assert.Empty(_db.EscalationDrafts.Where(d => d.TenantId == "t1"));
    }

    [Fact]
    public async Task RequestDeletion_CrossTenantIsolation()
    {
        _db.Tenants.Add(new TenantEntity
        {
            TenantId = "t2", DisplayName = "Other Tenant", IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.Sessions.Add(new SessionEntity
        {
            Id = Guid.NewGuid(), TenantId = "t2", UserId = "user-1", Title = "T2 Session",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        await _service.RequestDeletionAsync("t1", "user-1", "admin-1");

        // t2 data for same user-1 should be untouched.
        Assert.Equal(1, _db.Sessions.Count(s => s.TenantId == "t2"));
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
