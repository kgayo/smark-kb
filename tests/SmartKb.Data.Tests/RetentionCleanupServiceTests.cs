using Microsoft.Extensions.Logging.Abstractions;
using SmartKb.Contracts;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data.Entities;
using SmartKb.Data.Repositories;

namespace SmartKb.Data.Tests;

public class RetentionCleanupServiceTests : IDisposable
{
    private readonly SmartKbDbContext _db;
    private readonly StubAuditWriter _auditWriter;
    private readonly RetentionCleanupService _service;

    public RetentionCleanupServiceTests()
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

        _service = new RetentionCleanupService(
            _db, _auditWriter, NullLogger<RetentionCleanupService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task GetPolicies_Empty_ReturnsEmptyList()
    {
        var result = await _service.GetPoliciesAsync("t1");
        Assert.Equal("t1", result.TenantId);
        Assert.Empty(result.Policies);
    }

    [Fact]
    public async Task UpsertPolicy_CreatesNew()
    {
        var result = await _service.UpsertPolicyAsync("t1", new RetentionPolicyUpdateRequest
        {
            EntityType = "AppSession",
            RetentionDays = 90,
        }, "admin-1");

        Assert.Equal("AppSession", result.EntityType);
        Assert.Equal(90, result.RetentionDays);
    }

    [Fact]
    public async Task UpsertPolicy_UpdatesExisting()
    {
        await _service.UpsertPolicyAsync("t1", new RetentionPolicyUpdateRequest
        {
            EntityType = "AppSession",
            RetentionDays = 90,
        }, "admin-1");

        var result = await _service.UpsertPolicyAsync("t1", new RetentionPolicyUpdateRequest
        {
            EntityType = "AppSession",
            RetentionDays = 30,
        }, "admin-1");

        Assert.Equal(30, result.RetentionDays);

        var policies = await _service.GetPoliciesAsync("t1");
        Assert.Single(policies.Policies);
    }

    [Fact]
    public async Task UpsertPolicy_InvalidEntityType_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.UpsertPolicyAsync("t1", new RetentionPolicyUpdateRequest
            {
                EntityType = "InvalidType",
                RetentionDays = 30,
            }, "admin-1"));
    }

    [Fact]
    public async Task UpsertPolicy_ZeroDays_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.UpsertPolicyAsync("t1", new RetentionPolicyUpdateRequest
            {
                EntityType = "AppSession",
                RetentionDays = 0,
            }, "admin-1"));
    }

    [Fact]
    public async Task DeletePolicy_Existing_ReturnsTrue()
    {
        await _service.UpsertPolicyAsync("t1", new RetentionPolicyUpdateRequest
        {
            EntityType = "AppSession",
            RetentionDays = 90,
        }, "admin-1");

        var deleted = await _service.DeletePolicyAsync("t1", "AppSession", "admin-1");
        Assert.True(deleted);

        var policies = await _service.GetPoliciesAsync("t1");
        Assert.Empty(policies.Policies);
    }

    [Fact]
    public async Task DeletePolicy_NonExistent_ReturnsFalse()
    {
        var deleted = await _service.DeletePolicyAsync("t1", "AppSession", "admin-1");
        Assert.False(deleted);
    }

    [Fact]
    public async Task UpsertPolicy_EmitsAuditEvent()
    {
        await _service.UpsertPolicyAsync("t1", new RetentionPolicyUpdateRequest
        {
            EntityType = "AuditEvent",
            RetentionDays = 365,
        }, "admin-1");

        Assert.Contains(_auditWriter.Events,
            e => e.EventType == AuditEventTypes.RetentionPolicyUpdated && e.TenantId == "t1");
    }

    [Fact]
    public async Task ExecuteCleanup_NoPolicies_ReturnsEmpty()
    {
        var results = await _service.ExecuteCleanupAsync("t1");
        Assert.Empty(results);
    }

    [Fact]
    public async Task ExecuteCleanup_Sessions_SoftDeletesOldSessions()
    {
        await _service.UpsertPolicyAsync("t1", new RetentionPolicyUpdateRequest
        {
            EntityType = "AppSession",
            RetentionDays = 30,
        }, "admin-1");

        // Seed old and new sessions.
        _db.Sessions.Add(new SessionEntity
        {
            Id = Guid.NewGuid(), TenantId = "t1", UserId = "u1", Title = "Old Session",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-60),
        });
        _db.Sessions.Add(new SessionEntity
        {
            Id = Guid.NewGuid(), TenantId = "t1", UserId = "u1", Title = "New Session",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-5),
        });
        await _db.SaveChangesAsync();

        var results = await _service.ExecuteCleanupAsync("t1");

        Assert.Single(results);
        Assert.Equal("AppSession", results[0].EntityType);
        Assert.Equal(1, results[0].DeletedCount);
    }

    [Fact]
    public async Task ExecuteCleanup_AnswerTraces_HardDeletesOld()
    {
        await _service.UpsertPolicyAsync("t1", new RetentionPolicyUpdateRequest
        {
            EntityType = "AnswerTrace",
            RetentionDays = 30,
        }, "admin-1");

        _db.AnswerTraces.Add(new AnswerTraceEntity
        {
            Id = Guid.NewGuid(), TenantId = "t1", UserId = "u1", CorrelationId = "c1",
            Query = "test", ResponseType = "final_answer", ConfidenceLabel = "High",
            CitedChunkIds = "[]", RetrievedChunkIds = "[]", SystemPromptVersion = "v1",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-60),
        });
        _db.AnswerTraces.Add(new AnswerTraceEntity
        {
            Id = Guid.NewGuid(), TenantId = "t1", UserId = "u1", CorrelationId = "c2",
            Query = "test2", ResponseType = "final_answer", ConfidenceLabel = "Low",
            CitedChunkIds = "[]", RetrievedChunkIds = "[]", SystemPromptVersion = "v1",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-5),
        });
        await _db.SaveChangesAsync();

        var results = await _service.ExecuteCleanupAsync("t1");

        Assert.Single(results);
        Assert.Equal(1, results[0].DeletedCount);
        Assert.Equal(1, _db.AnswerTraces.Count());
    }

    [Fact]
    public async Task ExecuteCleanup_EmitsAuditEvent_WhenRecordsDeleted()
    {
        await _service.UpsertPolicyAsync("t1", new RetentionPolicyUpdateRequest
        {
            EntityType = "AppSession",
            RetentionDays = 1,
        }, "admin-1");
        _auditWriter.Events.Clear();

        _db.Sessions.Add(new SessionEntity
        {
            Id = Guid.NewGuid(), TenantId = "t1", UserId = "u1", Title = "Old",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10),
        });
        await _db.SaveChangesAsync();

        await _service.ExecuteCleanupAsync("t1");

        Assert.Contains(_auditWriter.Events,
            e => e.EventType == AuditEventTypes.RetentionCleanupExecuted);
    }

    [Fact]
    public async Task GetPolicies_MultiplePolicies_ReturnsSorted()
    {
        await _service.UpsertPolicyAsync("t1", new RetentionPolicyUpdateRequest { EntityType = "Message", RetentionDays = 60 }, "admin-1");
        await _service.UpsertPolicyAsync("t1", new RetentionPolicyUpdateRequest { EntityType = "AppSession", RetentionDays = 90 }, "admin-1");

        var result = await _service.GetPoliciesAsync("t1");

        Assert.Equal(2, result.Policies.Count);
        Assert.Equal("AppSession", result.Policies[0].EntityType);
        Assert.Equal("Message", result.Policies[1].EntityType);
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
