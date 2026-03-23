using Microsoft.Extensions.Logging.Abstractions;
using SmartKb.Contracts;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data.Entities;
using SmartKb.Data.Repositories;

namespace SmartKb.Data.Tests;

public class GoldCaseServiceTests : IDisposable
{
    private readonly SmartKbDbContext _db;
    private readonly StubAuditWriter _auditWriter;
    private readonly GoldCaseService _service;

    public GoldCaseServiceTests()
    {
        _db = TestDbContextFactory.Create();
        _auditWriter = new StubAuditWriter();

        _db.Tenants.Add(new TenantEntity { TenantId = "t1", DisplayName = "Test Tenant", IsActive = true, CreatedAt = DateTimeOffset.UtcNow });
        _db.Tenants.Add(new TenantEntity { TenantId = "t2", DisplayName = "Other Tenant", IsActive = true, CreatedAt = DateTimeOffset.UtcNow });
        _db.SaveChanges();

        _service = new GoldCaseService(_db, _auditWriter, NullLogger<GoldCaseService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private static CreateGoldCaseRequest MakeCreateRequest(string caseId = "eval-00100", string query = "How do I reset my password?") => new()
    {
        CaseId = caseId,
        Query = query,
        Expected = new GoldCaseExpected
        {
            ResponseType = "final_answer",
            MustInclude = ["password", "reset"],
            MustCiteSources = true,
            ShouldHaveEvidence = true,
        },
        Tags = ["auth", "password"],
    };

    // ── Create tests ──

    [Fact]
    public async Task Create_StoresAndReturnsDetail()
    {
        var result = await _service.CreateAsync("t1", MakeCreateRequest(), "admin-1");

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("eval-00100", result.CaseId);
        Assert.Equal("How do I reset my password?", result.Query);
        Assert.Equal("final_answer", result.Expected.ResponseType);
        Assert.Contains("password", result.Expected.MustInclude!);
        Assert.Contains("auth", result.Tags);
        Assert.Equal("admin-1", result.CreatedBy);
    }

    [Fact]
    public async Task Create_WritesAuditEvent()
    {
        await _service.CreateAsync("t1", MakeCreateRequest(), "admin-1");
        Assert.Contains(_auditWriter.Events, e => e.EventType == AuditEventTypes.GoldCaseCreated);
    }

    [Fact]
    public async Task Create_DuplicateCaseId_Throws()
    {
        await _service.CreateAsync("t1", MakeCreateRequest(), "admin-1");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateAsync("t1", MakeCreateRequest(), "admin-1"));
    }

    [Fact]
    public async Task Create_InvalidCaseIdFormat_Throws()
    {
        var req = MakeCreateRequest(caseId: "bad-id");
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.CreateAsync("t1", req, "admin-1"));
    }

    [Fact]
    public async Task Create_ShortQuery_Throws()
    {
        var req = MakeCreateRequest(query: "Hi");
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.CreateAsync("t1", req, "admin-1"));
    }

    [Fact]
    public async Task Create_InvalidResponseType_Throws()
    {
        var req = new CreateGoldCaseRequest
        {
            CaseId = "eval-00100",
            Query = "Valid query text",
            Expected = new GoldCaseExpected { ResponseType = "invalid_type" },
        };
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.CreateAsync("t1", req, "admin-1"));
    }

    [Fact]
    public async Task Create_WithContext_PersistsContext()
    {
        var req = new CreateGoldCaseRequest
        {
            CaseId = "eval-00100",
            Query = "Query with context",
            Expected = new GoldCaseExpected { ResponseType = "final_answer" },
            Context = new GoldCaseContext
            {
                ProductAreaHint = "Auth",
                CustomerRefs = ["customer:acme"],
            },
        };
        var result = await _service.CreateAsync("t1", req, "admin-1");
        Assert.NotNull(result.Context);
        Assert.Equal("Auth", result.Context!.ProductAreaHint);
        Assert.Contains("customer:acme", result.Context.CustomerRefs!);
    }

    // ── Get tests ──

    [Fact]
    public async Task Get_ReturnsDetail()
    {
        var created = await _service.CreateAsync("t1", MakeCreateRequest(), "admin-1");
        var result = await _service.GetAsync("t1", created.Id);
        Assert.NotNull(result);
        Assert.Equal(created.CaseId, result!.CaseId);
    }

    [Fact]
    public async Task Get_NotFound_ReturnsNull()
    {
        var result = await _service.GetAsync("t1", Guid.NewGuid());
        Assert.Null(result);
    }

    [Fact]
    public async Task Get_TenantIsolation()
    {
        var created = await _service.CreateAsync("t1", MakeCreateRequest(), "admin-1");
        var result = await _service.GetAsync("t2", created.Id);
        Assert.Null(result);
    }

    // ── List tests ──

    [Fact]
    public async Task List_ReturnsPaginated()
    {
        for (int i = 1; i <= 25; i++)
            await _service.CreateAsync("t1", MakeCreateRequest($"eval-{i:D5}"), "admin-1");

        var page1 = await _service.ListAsync("t1", page: 1, pageSize: 10);
        Assert.Equal(25, page1.TotalCount);
        Assert.Equal(10, page1.Cases.Count);
        Assert.True(page1.HasMore);

        var page3 = await _service.ListAsync("t1", page: 3, pageSize: 10);
        Assert.Equal(5, page3.Cases.Count);
        Assert.False(page3.HasMore);
    }

    [Fact]
    public async Task List_FilterByTag()
    {
        await _service.CreateAsync("t1", MakeCreateRequest(), "admin-1");
        await _service.CreateAsync("t1", new CreateGoldCaseRequest
        {
            CaseId = "eval-00200",
            Query = "Billing invoice question",
            Expected = new GoldCaseExpected { ResponseType = "final_answer" },
            Tags = ["billing"],
        }, "admin-1");

        var authCases = await _service.ListAsync("t1", tag: "auth");
        Assert.Equal(1, authCases.TotalCount);
        Assert.Equal("eval-00100", authCases.Cases[0].CaseId);
    }

    [Fact]
    public async Task List_TenantIsolation()
    {
        await _service.CreateAsync("t1", MakeCreateRequest(), "admin-1");
        var result = await _service.ListAsync("t2");
        Assert.Equal(0, result.TotalCount);
    }

    // ── Update tests ──

    [Fact]
    public async Task Update_ModifiesFields()
    {
        var created = await _service.CreateAsync("t1", MakeCreateRequest(), "admin-1");
        var result = await _service.UpdateAsync("t1", created.Id, new UpdateGoldCaseRequest
        {
            Query = "Updated query text here",
            Tags = ["auth", "sso"],
        }, "admin-2");

        Assert.NotNull(result);
        Assert.Equal("Updated query text here", result!.Query);
        Assert.Contains("sso", result.Tags);
        Assert.Equal("admin-2", result.UpdatedBy);
    }

    [Fact]
    public async Task Update_NotFound_ReturnsNull()
    {
        var result = await _service.UpdateAsync("t1", Guid.NewGuid(), new UpdateGoldCaseRequest { Query = "New query" }, "admin-1");
        Assert.Null(result);
    }

    [Fact]
    public async Task Update_WritesAuditEvent()
    {
        var created = await _service.CreateAsync("t1", MakeCreateRequest(), "admin-1");
        _auditWriter.Events.Clear();
        await _service.UpdateAsync("t1", created.Id, new UpdateGoldCaseRequest { Query = "Updated query text" }, "admin-1");
        Assert.Contains(_auditWriter.Events, e => e.EventType == AuditEventTypes.GoldCaseUpdated);
    }

    [Fact]
    public async Task Update_ShortQuery_Throws()
    {
        var created = await _service.CreateAsync("t1", MakeCreateRequest(), "admin-1");
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.UpdateAsync("t1", created.Id, new UpdateGoldCaseRequest { Query = "Hi" }, "admin-1"));
    }

    // ── Delete tests ──

    [Fact]
    public async Task Delete_RemovesCase()
    {
        var created = await _service.CreateAsync("t1", MakeCreateRequest(), "admin-1");
        var deleted = await _service.DeleteAsync("t1", created.Id, "admin-1");
        Assert.True(deleted);

        var result = await _service.GetAsync("t1", created.Id);
        Assert.Null(result);
    }

    [Fact]
    public async Task Delete_NotFound_ReturnsFalse()
    {
        var result = await _service.DeleteAsync("t1", Guid.NewGuid(), "admin-1");
        Assert.False(result);
    }

    [Fact]
    public async Task Delete_WritesAuditEvent()
    {
        var created = await _service.CreateAsync("t1", MakeCreateRequest(), "admin-1");
        _auditWriter.Events.Clear();
        await _service.DeleteAsync("t1", created.Id, "admin-1");
        Assert.Contains(_auditWriter.Events, e => e.EventType == AuditEventTypes.GoldCaseDeleted);
    }

    // ── Promote from feedback tests ──

    [Fact]
    public async Task PromoteFromFeedback_CreatesGoldCase()
    {
        // Setup: create session, message, and feedback.
        var session = new SessionEntity
        {
            Id = Guid.NewGuid(),
            TenantId = "t1",
            UserId = "user-1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.Sessions.Add(session);

        var userMsg = new MessageEntity
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            TenantId = "t1",
            Role = MessageRole.User,
            Content = "Why is my SSO login looping?",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.Messages.Add(userMsg);

        var assistantMsg = new MessageEntity
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            TenantId = "t1",
            Role = MessageRole.Assistant,
            Content = "This is the response.",
            CreatedAt = DateTimeOffset.UtcNow.AddSeconds(1),
        };
        _db.Messages.Add(assistantMsg);

        var feedback = new FeedbackEntity
        {
            Id = Guid.NewGuid(),
            MessageId = assistantMsg.Id,
            SessionId = session.Id,
            TenantId = "t1",
            UserId = "user-1",
            Type = Contracts.Enums.FeedbackType.ThumbsDown,
            CorrectionText = "The correct answer involves certificate rotation.",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.Feedbacks.Add(feedback);
        await _db.SaveChangesAsync();

        var result = await _service.PromoteFromFeedbackAsync("t1", new PromoteFromFeedbackRequest
        {
            FeedbackId = feedback.Id,
            CaseId = "eval-00300",
            Expected = new GoldCaseExpected
            {
                ResponseType = "final_answer",
                MustInclude = ["certificate", "rotation"],
            },
            Tags = ["auth", "sso"],
        }, "admin-1");

        Assert.Equal("eval-00300", result.CaseId);
        Assert.Equal("The correct answer involves certificate rotation.", result.Query);
        Assert.Equal(feedback.Id, result.SourceFeedbackId);
    }

    [Fact]
    public async Task PromoteFromFeedback_WritesAuditEvent()
    {
        var session = new SessionEntity { Id = Guid.NewGuid(), TenantId = "t1", UserId = "user-1", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        _db.Sessions.Add(session);
        var msg = new MessageEntity { Id = Guid.NewGuid(), SessionId = session.Id, TenantId = "t1", Role = MessageRole.Assistant, Content = "Response", CreatedAt = DateTimeOffset.UtcNow };
        _db.Messages.Add(msg);
        var fb = new FeedbackEntity { Id = Guid.NewGuid(), MessageId = msg.Id, SessionId = session.Id, TenantId = "t1", UserId = "u1", Type = Contracts.Enums.FeedbackType.ThumbsDown, Comment = "Wrong answer about auth", CreatedAt = DateTimeOffset.UtcNow };
        _db.Feedbacks.Add(fb);
        await _db.SaveChangesAsync();
        _auditWriter.Events.Clear();

        await _service.PromoteFromFeedbackAsync("t1", new PromoteFromFeedbackRequest
        {
            FeedbackId = fb.Id,
            CaseId = "eval-00301",
            Expected = new GoldCaseExpected { ResponseType = "final_answer" },
        }, "admin-1");

        Assert.Contains(_auditWriter.Events, e => e.EventType == AuditEventTypes.GoldCasePromoted);
    }

    [Fact]
    public async Task PromoteFromFeedback_FeedbackNotFound_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.PromoteFromFeedbackAsync("t1", new PromoteFromFeedbackRequest
            {
                FeedbackId = Guid.NewGuid(),
                CaseId = "eval-00400",
                Expected = new GoldCaseExpected { ResponseType = "final_answer" },
            }, "admin-1"));
    }

    // ── Export tests ──

    [Fact]
    public async Task Export_ReturnsJsonl()
    {
        await _service.CreateAsync("t1", MakeCreateRequest("eval-00100"), "admin-1");
        await _service.CreateAsync("t1", new CreateGoldCaseRequest
        {
            CaseId = "eval-00200",
            Query = "Second query for export",
            Expected = new GoldCaseExpected { ResponseType = "next_steps_only" },
        }, "admin-1");

        var jsonl = await _service.ExportAsJsonlAsync("t1");
        var lines = jsonl.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        Assert.Equal(2, lines.Count);
        Assert.Contains("eval-00100", lines[0]);
        Assert.Contains("eval-00200", lines[1]);
    }

    [Fact]
    public async Task Export_TenantIsolation()
    {
        await _service.CreateAsync("t1", MakeCreateRequest(), "admin-1");
        var jsonl = await _service.ExportAsJsonlAsync("t2");
        Assert.Empty(jsonl);
    }

    // ── Stubs ──

    private sealed class StubAuditWriter : IAuditEventWriter
    {
        public List<(string TenantId, string EventType, string ActorId, string CorrelationId, string Detail)> Events { get; } = new();

        public Task WriteAsync(string tenantId, string eventType, string actorId, string correlationId, string detail, CancellationToken ct = default)
        {
            Events.Add((tenantId, eventType, actorId, correlationId, detail));
            return Task.CompletedTask;
        }
    }
}
