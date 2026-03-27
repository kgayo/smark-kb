using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartKb.Contracts;
using SmartKb.Contracts.Configuration;
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
            _db, _auditWriter, NullLogger<RetentionCleanupService>.Instance,
            Options.Create(new RetentionSettings()));
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
    public async Task UpsertPolicy_WithMetricRetentionDays()
    {
        var result = await _service.UpsertPolicyAsync("t1", new RetentionPolicyUpdateRequest
        {
            EntityType = "AuditEvent",
            RetentionDays = 30,
            MetricRetentionDays = 365,
        }, "admin-1");

        Assert.Equal(30, result.RetentionDays);
        Assert.Equal(365, result.MetricRetentionDays);
    }

    [Fact]
    public async Task UpsertPolicy_MetricRetentionDaysLessThanRetentionDays_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.UpsertPolicyAsync("t1", new RetentionPolicyUpdateRequest
            {
                EntityType = "AuditEvent",
                RetentionDays = 90,
                MetricRetentionDays = 30,
            }, "admin-1"));
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

        var oldTime = DateTimeOffset.UtcNow.AddDays(-60);
        var newTime = DateTimeOffset.UtcNow.AddDays(-5);
        _db.Sessions.Add(new SessionEntity
        {
            Id = Guid.NewGuid(), TenantId = "t1", UserId = "u1", Title = "Old Session",
            CreatedAt = oldTime, CreatedAtEpoch = oldTime.ToUnixTimeSeconds(),
        });
        _db.Sessions.Add(new SessionEntity
        {
            Id = Guid.NewGuid(), TenantId = "t1", UserId = "u1", Title = "New Session",
            CreatedAt = newTime, CreatedAtEpoch = newTime.ToUnixTimeSeconds(),
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

        var oldTime = DateTimeOffset.UtcNow.AddDays(-60);
        var newTime = DateTimeOffset.UtcNow.AddDays(-5);
        _db.AnswerTraces.Add(new AnswerTraceEntity
        {
            Id = Guid.NewGuid(), TenantId = "t1", UserId = "u1", CorrelationId = "c1",
            Query = "test", ResponseType = "final_answer", ConfidenceLabel = "High",
            CitedChunkIds = "[]", RetrievedChunkIds = "[]", SystemPromptVersion = "v1",
            CreatedAt = oldTime, CreatedAtEpoch = oldTime.ToUnixTimeSeconds(),
        });
        _db.AnswerTraces.Add(new AnswerTraceEntity
        {
            Id = Guid.NewGuid(), TenantId = "t1", UserId = "u1", CorrelationId = "c2",
            Query = "test2", ResponseType = "final_answer", ConfidenceLabel = "Low",
            CitedChunkIds = "[]", RetrievedChunkIds = "[]", SystemPromptVersion = "v1",
            CreatedAt = newTime, CreatedAtEpoch = newTime.ToUnixTimeSeconds(),
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

        var oldTime = DateTimeOffset.UtcNow.AddDays(-10);
        _db.Sessions.Add(new SessionEntity
        {
            Id = Guid.NewGuid(), TenantId = "t1", UserId = "u1", Title = "Old",
            CreatedAt = oldTime, CreatedAtEpoch = oldTime.ToUnixTimeSeconds(),
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

    // ──── P2-005: Execution history tests ────

    [Fact]
    public async Task ExecuteCleanup_PersistsExecutionLog()
    {
        await _service.UpsertPolicyAsync("t1", new RetentionPolicyUpdateRequest
        {
            EntityType = "AppSession",
            RetentionDays = 30,
        }, "admin-1");

        var oldTime = DateTimeOffset.UtcNow.AddDays(-60);
        _db.Sessions.Add(new SessionEntity
        {
            Id = Guid.NewGuid(), TenantId = "t1", UserId = "u1", Title = "Old",
            CreatedAt = oldTime, CreatedAtEpoch = oldTime.ToUnixTimeSeconds(),
        });
        await _db.SaveChangesAsync();

        await _service.ExecuteCleanupAsync("t1");

        var logs = _db.RetentionExecutionLogs.Where(l => l.TenantId == "t1").ToList();
        Assert.Single(logs);
        Assert.Equal("AppSession", logs[0].EntityType);
        Assert.Equal(1, logs[0].DeletedCount);
        Assert.True(logs[0].DurationMs >= 0);
        Assert.Equal("system", logs[0].ActorId);
    }

    [Fact]
    public async Task ExecuteCleanup_ZeroDeleted_StillLogsExecution()
    {
        await _service.UpsertPolicyAsync("t1", new RetentionPolicyUpdateRequest
        {
            EntityType = "AppSession",
            RetentionDays = 30,
        }, "admin-1");

        await _service.ExecuteCleanupAsync("t1");

        var logs = _db.RetentionExecutionLogs.Where(l => l.TenantId == "t1").ToList();
        Assert.Single(logs);
        Assert.Equal(0, logs[0].DeletedCount);
    }

    [Fact]
    public async Task GetExecutionHistory_ReturnsLogs()
    {
        await _service.UpsertPolicyAsync("t1", new RetentionPolicyUpdateRequest
        {
            EntityType = "AppSession",
            RetentionDays = 30,
        }, "admin-1");

        await _service.ExecuteCleanupAsync("t1");
        await _service.ExecuteCleanupAsync("t1");

        var history = await _service.GetExecutionHistoryAsync("t1");

        Assert.Equal(2, history.TotalCount);
        Assert.Equal(2, history.Entries.Count);
    }

    [Fact]
    public async Task GetExecutionHistory_FilterByEntityType()
    {
        await _service.UpsertPolicyAsync("t1", new RetentionPolicyUpdateRequest { EntityType = "AppSession", RetentionDays = 30 }, "admin-1");
        await _service.UpsertPolicyAsync("t1", new RetentionPolicyUpdateRequest { EntityType = "Message", RetentionDays = 30 }, "admin-1");

        await _service.ExecuteCleanupAsync("t1");

        var history = await _service.GetExecutionHistoryAsync("t1", entityType: "AppSession");

        Assert.Equal(1, history.TotalCount);
        Assert.All(history.Entries, e => Assert.Equal("AppSession", e.EntityType));
    }

    [Fact]
    public async Task GetExecutionHistory_Pagination()
    {
        await _service.UpsertPolicyAsync("t1", new RetentionPolicyUpdateRequest { EntityType = "AppSession", RetentionDays = 30 }, "admin-1");

        await _service.ExecuteCleanupAsync("t1");
        await _service.ExecuteCleanupAsync("t1");
        await _service.ExecuteCleanupAsync("t1");

        var page1 = await _service.GetExecutionHistoryAsync("t1", skip: 0, take: 2);
        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(2, page1.Entries.Count);

        var page2 = await _service.GetExecutionHistoryAsync("t1", skip: 2, take: 2);
        Assert.Equal(3, page2.TotalCount);
        Assert.Single(page2.Entries);
    }

    [Fact]
    public async Task GetExecutionHistory_OrderedByMostRecent()
    {
        await _service.UpsertPolicyAsync("t1", new RetentionPolicyUpdateRequest { EntityType = "AppSession", RetentionDays = 30 }, "admin-1");

        await _service.ExecuteCleanupAsync("t1");
        await _service.ExecuteCleanupAsync("t1");

        var history = await _service.GetExecutionHistoryAsync("t1");

        Assert.True(history.Entries[0].ExecutedAt >= history.Entries[1].ExecutedAt);
    }

    [Fact]
    public async Task GetExecutionHistory_TenantIsolation()
    {
        _db.Tenants.Add(new TenantEntity { TenantId = "t2", DisplayName = "Other", IsActive = true, CreatedAt = DateTimeOffset.UtcNow });
        await _db.SaveChangesAsync();

        await _service.UpsertPolicyAsync("t1", new RetentionPolicyUpdateRequest { EntityType = "AppSession", RetentionDays = 30 }, "admin-1");
        await _service.ExecuteCleanupAsync("t1");

        var history = await _service.GetExecutionHistoryAsync("t2");
        Assert.Equal(0, history.TotalCount);
    }

    // ──── P2-005: Compliance report tests ────

    [Fact]
    public async Task GetComplianceReport_NoPolicies_ReturnsEmptyNotCompliant()
    {
        var report = await _service.GetComplianceReportAsync("t1");

        Assert.Equal("t1", report.TenantId);
        Assert.False(report.IsCompliant);
        Assert.Equal(0, report.TotalPolicies);
        Assert.Equal(0, report.OverduePolicies);
        Assert.Empty(report.Entries);
    }

    [Fact]
    public async Task GetComplianceReport_RecentExecution_IsCompliant()
    {
        await _service.UpsertPolicyAsync("t1", new RetentionPolicyUpdateRequest { EntityType = "AppSession", RetentionDays = 30 }, "admin-1");
        await _service.ExecuteCleanupAsync("t1");

        var report = await _service.GetComplianceReportAsync("t1");

        Assert.True(report.IsCompliant);
        Assert.Equal(1, report.TotalPolicies);
        Assert.Equal(0, report.OverduePolicies);
        Assert.Single(report.Entries);
        Assert.False(report.Entries[0].IsOverdue);
    }

    [Fact]
    public async Task GetComplianceReport_NeverExecuted_IsOverdue()
    {
        await _service.UpsertPolicyAsync("t1", new RetentionPolicyUpdateRequest { EntityType = "AppSession", RetentionDays = 30 }, "admin-1");

        var report = await _service.GetComplianceReportAsync("t1");

        Assert.False(report.IsCompliant);
        Assert.Equal(1, report.OverduePolicies);
        Assert.True(report.Entries[0].IsOverdue);
        Assert.Equal(-1, report.Entries[0].DaysSinceLastExecution);
    }

    [Fact]
    public async Task GetComplianceReport_EmitsAuditEvent()
    {
        await _service.UpsertPolicyAsync("t1", new RetentionPolicyUpdateRequest { EntityType = "AppSession", RetentionDays = 30 }, "admin-1");
        _auditWriter.Events.Clear();

        await _service.GetComplianceReportAsync("t1");

        Assert.Contains(_auditWriter.Events,
            e => e.EventType == AuditEventTypes.RetentionComplianceChecked);
    }

    [Fact]
    public async Task GetComplianceReport_IncludesMetricRetentionDays()
    {
        await _service.UpsertPolicyAsync("t1", new RetentionPolicyUpdateRequest
        {
            EntityType = "AuditEvent",
            RetentionDays = 30,
            MetricRetentionDays = 365,
        }, "admin-1");

        var report = await _service.GetComplianceReportAsync("t1");

        Assert.Equal(365, report.Entries[0].MetricRetentionDays);
    }

    [Fact]
    public async Task GetComplianceReport_MixedCompliance()
    {
        await _service.UpsertPolicyAsync("t1", new RetentionPolicyUpdateRequest { EntityType = "AppSession", RetentionDays = 30 }, "admin-1");
        await _service.UpsertPolicyAsync("t1", new RetentionPolicyUpdateRequest { EntityType = "Message", RetentionDays = 60 }, "admin-1");

        // Only execute cleanup for AppSession (by running full cleanup, both get logged).
        await _service.ExecuteCleanupAsync("t1");

        var report = await _service.GetComplianceReportAsync("t1");

        // Both were executed, so both should be compliant.
        Assert.True(report.IsCompliant);
        Assert.Equal(2, report.TotalPolicies);
        Assert.Equal(0, report.OverduePolicies);
    }

    // ──── TECH-151: Epoch-based server-side filtering tests ────

    [Fact]
    public async Task ExecuteCleanup_Sessions_FiltersServerSideViaEpochColumn()
    {
        await _service.UpsertPolicyAsync("t1", new RetentionPolicyUpdateRequest
        {
            EntityType = "AppSession",
            RetentionDays = 30,
        }, "admin-1");

        var oldTime = DateTimeOffset.UtcNow.AddDays(-60);
        var newTime = DateTimeOffset.UtcNow.AddDays(-5);

        _db.Sessions.AddRange(
            new SessionEntity
            {
                Id = Guid.NewGuid(), TenantId = "t1", UserId = "u1", Title = "Old",
                CreatedAt = oldTime, CreatedAtEpoch = oldTime.ToUnixTimeSeconds(),
            },
            new SessionEntity
            {
                Id = Guid.NewGuid(), TenantId = "t1", UserId = "u1", Title = "Recent",
                CreatedAt = newTime, CreatedAtEpoch = newTime.ToUnixTimeSeconds(),
            });
        await _db.SaveChangesAsync();

        var results = await _service.ExecuteCleanupAsync("t1");

        Assert.Equal(1, results[0].DeletedCount);
        // Only the old session should be soft-deleted; the recent one remains active.
        Assert.Equal(1, _db.Sessions.Count());
    }

    [Fact]
    public async Task ExecuteCleanup_Messages_FiltersServerSideViaEpochColumn()
    {
        await _service.UpsertPolicyAsync("t1", new RetentionPolicyUpdateRequest
        {
            EntityType = "Message",
            RetentionDays = 30,
        }, "admin-1");

        var sessionId = Guid.NewGuid();
        _db.Sessions.Add(new SessionEntity
        {
            Id = sessionId, TenantId = "t1", UserId = "u1", Title = "S",
            CreatedAt = DateTimeOffset.UtcNow, CreatedAtEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        });

        var oldTime = DateTimeOffset.UtcNow.AddDays(-60);
        var newTime = DateTimeOffset.UtcNow.AddDays(-5);

        _db.Messages.AddRange(
            new MessageEntity
            {
                Id = Guid.NewGuid(), SessionId = sessionId, TenantId = "t1",
                Role = Contracts.Enums.MessageRole.User, Content = "old",
                CreatedAt = oldTime, CreatedAtEpoch = oldTime.ToUnixTimeSeconds(),
            },
            new MessageEntity
            {
                Id = Guid.NewGuid(), SessionId = sessionId, TenantId = "t1",
                Role = Contracts.Enums.MessageRole.User, Content = "new",
                CreatedAt = newTime, CreatedAtEpoch = newTime.ToUnixTimeSeconds(),
            });
        await _db.SaveChangesAsync();

        var results = await _service.ExecuteCleanupAsync("t1");

        Assert.Equal(1, results[0].DeletedCount);
        Assert.Equal(1, _db.Messages.Count());
    }

    [Fact]
    public async Task ExecuteCleanup_AuditEvents_FiltersServerSideViaEpochColumn()
    {
        await _service.UpsertPolicyAsync("t1", new RetentionPolicyUpdateRequest
        {
            EntityType = "AuditEvent",
            RetentionDays = 30,
        }, "admin-1");

        var oldTime = DateTimeOffset.UtcNow.AddDays(-60);
        var newTime = DateTimeOffset.UtcNow.AddDays(-5);

        _db.AuditEvents.AddRange(
            new AuditEventEntity
            {
                Id = Guid.NewGuid(), TenantId = "t1", EventType = "test", ActorId = "a",
                CorrelationId = "c1", Detail = "old", Timestamp = oldTime,
                TimestampEpoch = oldTime.ToUnixTimeSeconds(),
            },
            new AuditEventEntity
            {
                Id = Guid.NewGuid(), TenantId = "t1", EventType = "test", ActorId = "a",
                CorrelationId = "c2", Detail = "new", Timestamp = newTime,
                TimestampEpoch = newTime.ToUnixTimeSeconds(),
            });
        await _db.SaveChangesAsync();

        var results = await _service.ExecuteCleanupAsync("t1");

        Assert.Equal(1, results[0].DeletedCount);
        // Hard delete — only the new event remains.
        var remaining = _db.AuditEvents.Where(a => a.TenantId == "t1" && a.EventType == "test").ToList();
        Assert.Single(remaining);
        Assert.Equal("new", remaining[0].Detail);
    }

    [Fact]
    public async Task ExecuteCleanup_EvidenceChunks_FiltersServerSideViaEpochColumn()
    {
        await _service.UpsertPolicyAsync("t1", new RetentionPolicyUpdateRequest
        {
            EntityType = "EvidenceChunk",
            RetentionDays = 30,
        }, "admin-1");

        // Need a connector for FK.
        var connectorId = Guid.NewGuid();
        _db.Connectors.Add(new ConnectorEntity
        {
            Id = connectorId, TenantId = "t1", Name = "test",
            ConnectorType = Contracts.Enums.ConnectorType.AzureDevOps,
            Status = Contracts.Enums.ConnectorStatus.Enabled,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });

        var oldTime = DateTimeOffset.UtcNow.AddDays(-60);
        var newTime = DateTimeOffset.UtcNow.AddDays(-5);

        _db.EvidenceChunks.AddRange(
            new EvidenceChunkEntity
            {
                ChunkId = "old_chunk", EvidenceId = "e1", TenantId = "t1",
                ConnectorId = connectorId, ChunkIndex = 0, ChunkText = "old",
                SourceSystem = "ado", SourceType = "ticket", Status = "active",
                Visibility = "public", AccessLabel = "all", Title = "Old",
                SourceUrl = "https://example.com", ContentHash = "h1",
                UpdatedAt = oldTime, CreatedAt = oldTime, CreatedAtEpoch = oldTime.ToUnixTimeSeconds(),
            },
            new EvidenceChunkEntity
            {
                ChunkId = "new_chunk", EvidenceId = "e2", TenantId = "t1",
                ConnectorId = connectorId, ChunkIndex = 0, ChunkText = "new",
                SourceSystem = "ado", SourceType = "ticket", Status = "active",
                Visibility = "public", AccessLabel = "all", Title = "New",
                SourceUrl = "https://example.com", ContentHash = "h2",
                UpdatedAt = newTime, CreatedAt = newTime, CreatedAtEpoch = newTime.ToUnixTimeSeconds(),
            });
        await _db.SaveChangesAsync();

        var results = await _service.ExecuteCleanupAsync("t1");

        Assert.Equal(1, results[0].DeletedCount);
        Assert.Equal(1, _db.EvidenceChunks.Count());
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
