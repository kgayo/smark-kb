using Microsoft.Extensions.Logging.Abstractions;
using SmartKb.Contracts;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data.Entities;
using SmartKb.Data.Repositories;

namespace SmartKb.Data.Tests;

public class PatternMaintenanceServiceTests : IDisposable
{
    private readonly SmartKbDbContext _db;
    private readonly StubAuditWriter _auditWriter;
    private readonly PatternMaintenanceSettings _settings;
    private readonly PatternMaintenanceService _service;

    private const string TenantId = "t1";
    private const string ActorId = "admin-1";
    private const string CorrelationId = "corr-1";

    public PatternMaintenanceServiceTests()
    {
        _db = TestDbContextFactory.Create();
        _auditWriter = new StubAuditWriter();
        _settings = new PatternMaintenanceSettings();
        SeedTenant();
        _service = new PatternMaintenanceService(
            _db, _auditWriter, _settings,
            NullLogger<PatternMaintenanceService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private void SeedTenant()
    {
        _db.Tenants.Add(new TenantEntity
        {
            TenantId = TenantId,
            DisplayName = "Test",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();
    }

    private CasePatternEntity SeedPattern(
        string trustLevel = "Approved",
        float? qualityScore = 0.8f,
        int daysOld = 5,
        string? productArea = "Backend")
    {
        var entity = new CasePatternEntity
        {
            Id = Guid.NewGuid(),
            PatternId = $"pattern-{Guid.NewGuid():N}",
            TenantId = TenantId,
            Title = "Test Pattern",
            ProblemStatement = "Test problem statement for maintenance",
            SymptomsJson = "[\"error\"]",
            DiagnosisStepsJson = "[\"check logs\"]",
            ResolutionStepsJson = "[\"restart service\"]",
            VerificationStepsJson = "[\"verify running\"]",
            EscalationCriteriaJson = "[]",
            RelatedEvidenceIdsJson = "[\"ev1\"]",
            Confidence = 0.7f,
            TrustLevel = trustLevel,
            Version = 1,
            ProductArea = productArea,
            TagsJson = "[]",
            Visibility = "Internal",
            AllowedGroupsJson = "[]",
            AccessLabel = "Internal",
            SourceUrl = "session://test",
            ApplicabilityConstraintsJson = "[]",
            ExclusionsJson = "[]",
            QualityScore = qualityScore,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-daysOld),
            CreatedAtEpoch = DateTimeOffset.UtcNow.AddDays(-daysOld).ToUnixTimeSeconds(),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-daysOld),
        };
        _db.CasePatterns.Add(entity);
        _db.SaveChanges();
        return entity;
    }

    private void SeedAnswerTrace(string tenantId, string citedChunkIdsJson, int daysOld = 0)
    {
        _db.Set<AnswerTraceEntity>().Add(new AnswerTraceEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = "user-1",
            CorrelationId = $"trace-{Guid.NewGuid():N}",
            Query = "test query",
            ResponseType = "Grounded",
            ConfidenceLabel = "High",
            Confidence = 0.9f,
            CitedChunkIds = citedChunkIdsJson,
            RetrievedChunkIds = "[]",
            SystemPromptVersion = "v1",
            DurationMs = 100,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-daysOld),
            CreatedAtEpoch = DateTimeOffset.UtcNow.AddDays(-daysOld).ToUnixTimeSeconds(),
        });
        _db.SaveChanges();
    }

    // --- Stale Detection ---

    [Fact]
    public async Task DetectMaintenance_StalePattern_CreatesTask()
    {
        _settings.StaleDaysThreshold = 30;
        SeedPattern(daysOld: 60);

        var result = await _service.DetectMaintenanceIssuesAsync(TenantId, ActorId, CorrelationId);

        Assert.Equal(1, result.PatternsScanned);
        Assert.True(result.StaleDetected > 0);
        Assert.True(result.TasksCreated > 0);

        var tasks = _db.PatternMaintenanceTasks.Where(t => t.TenantId == TenantId).ToList();
        Assert.Contains(tasks, t => t.TaskType == "Stale");
    }

    [Fact]
    public async Task DetectMaintenance_RecentPattern_NoStaleTask()
    {
        _settings.StaleDaysThreshold = 90;
        SeedPattern(daysOld: 10);

        var result = await _service.DetectMaintenanceIssuesAsync(TenantId, ActorId, CorrelationId);

        Assert.Equal(0, result.StaleDetected);
    }

    [Fact]
    public async Task DetectMaintenance_VeryStalePattern_CriticalSeverity()
    {
        _settings.StaleDaysThreshold = 30;
        SeedPattern(daysOld: 120); // 4x the threshold.

        await _service.DetectMaintenanceIssuesAsync(TenantId, ActorId, CorrelationId);

        var task = _db.PatternMaintenanceTasks
            .First(t => t.TenantId == TenantId && t.TaskType == "Stale");
        Assert.Equal("Critical", task.Severity);
    }

    // --- Low Quality Detection ---

    [Fact]
    public async Task DetectMaintenance_LowQuality_CreatesTask()
    {
        _settings.LowQualityThreshold = 0.5f;
        SeedPattern(qualityScore: 0.3f);

        var result = await _service.DetectMaintenanceIssuesAsync(TenantId, ActorId, CorrelationId);

        Assert.Equal(1, result.LowQualityDetected);
        var tasks = _db.PatternMaintenanceTasks.Where(t => t.TenantId == TenantId).ToList();
        Assert.Contains(tasks, t => t.TaskType == "LowQuality");
    }

    [Fact]
    public async Task DetectMaintenance_HighQuality_NoTask()
    {
        _settings.LowQualityThreshold = 0.4f;
        SeedPattern(qualityScore: 0.8f);

        var result = await _service.DetectMaintenanceIssuesAsync(TenantId, ActorId, CorrelationId);

        Assert.Equal(0, result.LowQualityDetected);
    }

    [Fact]
    public async Task DetectMaintenance_NullQualityScore_NoTask()
    {
        _settings.LowQualityThreshold = 0.5f;
        SeedPattern(qualityScore: null);

        var result = await _service.DetectMaintenanceIssuesAsync(TenantId, ActorId, CorrelationId);

        Assert.Equal(0, result.LowQualityDetected);
    }

    [Fact]
    public async Task DetectMaintenance_VeryLowQuality_CriticalSeverity()
    {
        _settings.LowQualityThreshold = 0.5f;
        SeedPattern(qualityScore: 0.15f);

        await _service.DetectMaintenanceIssuesAsync(TenantId, ActorId, CorrelationId);

        var task = _db.PatternMaintenanceTasks
            .First(t => t.TenantId == TenantId && t.TaskType == "LowQuality");
        Assert.Equal("Critical", task.Severity);
    }

    // --- Unused Detection ---

    [Fact]
    public async Task DetectMaintenance_UnusedPattern_CreatesTask()
    {
        _settings.UnusedDaysThreshold = 30;
        SeedPattern();
        // No answer traces referencing this pattern.

        var result = await _service.DetectMaintenanceIssuesAsync(TenantId, ActorId, CorrelationId);

        Assert.True(result.UnusedDetected > 0);
    }

    [Fact]
    public async Task DetectMaintenance_RecentlyUsedPattern_NoUnusedTask()
    {
        _settings.UnusedDaysThreshold = 30;
        var pattern = SeedPattern();
        SeedAnswerTrace(TenantId, $"[\"{pattern.PatternId}\"]", daysOld: 5);

        var result = await _service.DetectMaintenanceIssuesAsync(TenantId, ActorId, CorrelationId);

        Assert.Equal(0, result.UnusedDetected);
    }

    // --- Duplicate Prevention ---

    [Fact]
    public async Task DetectMaintenance_NoDoubleTaskCreation()
    {
        _settings.StaleDaysThreshold = 30;
        SeedPattern(daysOld: 60);

        await _service.DetectMaintenanceIssuesAsync(TenantId, ActorId, CorrelationId);
        var firstCount = _db.PatternMaintenanceTasks.Count(t => t.TenantId == TenantId);

        await _service.DetectMaintenanceIssuesAsync(TenantId, ActorId, CorrelationId);
        var secondCount = _db.PatternMaintenanceTasks.Count(t => t.TenantId == TenantId);

        Assert.Equal(firstCount, secondCount);
    }

    // --- Deprecated Patterns Excluded ---

    [Fact]
    public async Task DetectMaintenance_DeprecatedPatterns_Excluded()
    {
        SeedPattern(trustLevel: "Deprecated", daysOld: 200, qualityScore: 0.1f);

        var result = await _service.DetectMaintenanceIssuesAsync(TenantId, ActorId, CorrelationId);

        Assert.Equal(0, result.PatternsScanned);
        Assert.Equal(0, result.TasksCreated);
    }

    // --- Audit Event ---

    [Fact]
    public async Task DetectMaintenance_WritesAuditEvent()
    {
        await _service.DetectMaintenanceIssuesAsync(TenantId, ActorId, CorrelationId);

        Assert.Contains(_auditWriter.Events,
            e => e.EventType == AuditEventTypes.MaintenanceDetectionRun);
    }

    // --- GetMaintenanceTasks ---

    [Fact]
    public async Task GetMaintenanceTasks_Pagination()
    {
        for (int i = 0; i < 5; i++)
        {
            _db.PatternMaintenanceTasks.Add(new PatternMaintenanceTaskEntity
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                PatternId = $"pattern-{i}",
                TaskType = "Stale",
                Severity = "Warning",
                Description = $"Task {i}",
                RecommendedAction = "Review",
                MetricsJson = "{}",
                Status = "Pending",
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-i),
                CreatedAtEpoch = DateTimeOffset.UtcNow.AddMinutes(-i).ToUnixTimeSeconds(),
            });
        }
        _db.SaveChanges();

        var page1 = await _service.GetMaintenanceTasksAsync(TenantId, null, null, 1, 3);
        Assert.Equal(5, page1.TotalCount);
        Assert.Equal(3, page1.Tasks.Count);
        Assert.True(page1.HasMore);
    }

    [Fact]
    public async Task GetMaintenanceTasks_FilterByStatus()
    {
        _db.PatternMaintenanceTasks.Add(new PatternMaintenanceTaskEntity
        {
            Id = Guid.NewGuid(), TenantId = TenantId, PatternId = "p1",
            TaskType = "Stale", Severity = "Warning",
            Description = "Stale", RecommendedAction = "Review",
            MetricsJson = "{}", Status = "Pending",
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedAtEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        });
        _db.PatternMaintenanceTasks.Add(new PatternMaintenanceTaskEntity
        {
            Id = Guid.NewGuid(), TenantId = TenantId, PatternId = "p2",
            TaskType = "LowQuality", Severity = "Warning",
            Description = "Low", RecommendedAction = "Improve",
            MetricsJson = "{}", Status = "Resolved",
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedAtEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        });
        _db.SaveChanges();

        var pending = await _service.GetMaintenanceTasksAsync(TenantId, "Pending", null, 1, 20);
        Assert.Single(pending.Tasks);

        var resolved = await _service.GetMaintenanceTasksAsync(TenantId, "Resolved", null, 1, 20);
        Assert.Single(resolved.Tasks);
    }

    [Fact]
    public async Task GetMaintenanceTasks_FilterByTaskType()
    {
        _db.PatternMaintenanceTasks.Add(new PatternMaintenanceTaskEntity
        {
            Id = Guid.NewGuid(), TenantId = TenantId, PatternId = "p1",
            TaskType = "Stale", Severity = "Warning",
            Description = "Stale", RecommendedAction = "Review",
            MetricsJson = "{}", Status = "Pending",
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedAtEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        });
        _db.PatternMaintenanceTasks.Add(new PatternMaintenanceTaskEntity
        {
            Id = Guid.NewGuid(), TenantId = TenantId, PatternId = "p2",
            TaskType = "LowQuality", Severity = "Warning",
            Description = "Low", RecommendedAction = "Improve",
            MetricsJson = "{}", Status = "Pending",
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedAtEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        });
        _db.SaveChanges();

        var stale = await _service.GetMaintenanceTasksAsync(TenantId, null, "Stale", 1, 20);
        Assert.Single(stale.Tasks);
        Assert.Equal("Stale", stale.Tasks[0].TaskType);
    }

    // --- ResolveTask / DismissTask ---

    [Fact]
    public async Task ResolveTask_UpdatesStatus()
    {
        var entity = new PatternMaintenanceTaskEntity
        {
            Id = Guid.NewGuid(), TenantId = TenantId, PatternId = "p1",
            TaskType = "Stale", Severity = "Warning",
            Description = "Stale", RecommendedAction = "Review",
            MetricsJson = "{}", Status = "Pending",
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedAtEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };
        _db.PatternMaintenanceTasks.Add(entity);
        _db.SaveChanges();

        var result = await _service.ResolveTaskAsync(
            entity.Id, TenantId, ActorId, CorrelationId,
            new ResolveMaintenanceTaskRequest { Notes = "Reviewed and confirmed" });

        Assert.NotNull(result);
        Assert.Equal("Resolved", result!.Status);
        Assert.Equal(ActorId, result.ResolvedBy);
    }

    [Fact]
    public async Task DismissTask_UpdatesStatus()
    {
        var entity = new PatternMaintenanceTaskEntity
        {
            Id = Guid.NewGuid(), TenantId = TenantId, PatternId = "p1",
            TaskType = "Unused", Severity = "Info",
            Description = "Not used", RecommendedAction = "Review",
            MetricsJson = "{}", Status = "Pending",
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedAtEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };
        _db.PatternMaintenanceTasks.Add(entity);
        _db.SaveChanges();

        var result = await _service.DismissTaskAsync(
            entity.Id, TenantId, ActorId, CorrelationId,
            new ResolveMaintenanceTaskRequest { Notes = "Expected behavior" });

        Assert.NotNull(result);
        Assert.Equal("Dismissed", result!.Status);
    }

    [Fact]
    public async Task ResolveTask_AlreadyResolved_ReturnsNull()
    {
        var entity = new PatternMaintenanceTaskEntity
        {
            Id = Guid.NewGuid(), TenantId = TenantId, PatternId = "p1",
            TaskType = "Stale", Severity = "Warning",
            Description = "Stale", RecommendedAction = "Review",
            MetricsJson = "{}", Status = "Resolved",
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedAtEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };
        _db.PatternMaintenanceTasks.Add(entity);
        _db.SaveChanges();

        var result = await _service.ResolveTaskAsync(
            entity.Id, TenantId, ActorId, CorrelationId,
            new ResolveMaintenanceTaskRequest());

        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveTask_WrongTenant_ReturnsNull()
    {
        var entity = new PatternMaintenanceTaskEntity
        {
            Id = Guid.NewGuid(), TenantId = TenantId, PatternId = "p1",
            TaskType = "Stale", Severity = "Warning",
            Description = "Stale", RecommendedAction = "Review",
            MetricsJson = "{}", Status = "Pending",
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedAtEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };
        _db.PatternMaintenanceTasks.Add(entity);
        _db.SaveChanges();

        var result = await _service.ResolveTaskAsync(
            entity.Id, "other-tenant", ActorId, CorrelationId,
            new ResolveMaintenanceTaskRequest());

        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveTask_WritesAuditEvent()
    {
        var entity = new PatternMaintenanceTaskEntity
        {
            Id = Guid.NewGuid(), TenantId = TenantId, PatternId = "p1",
            TaskType = "Stale", Severity = "Warning",
            Description = "Stale", RecommendedAction = "Review",
            MetricsJson = "{}", Status = "Pending",
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedAtEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };
        _db.PatternMaintenanceTasks.Add(entity);
        _db.SaveChanges();

        await _service.ResolveTaskAsync(
            entity.Id, TenantId, ActorId, CorrelationId,
            new ResolveMaintenanceTaskRequest());

        Assert.Contains(_auditWriter.Events,
            e => e.EventType == AuditEventTypes.MaintenanceTaskResolved);
    }

    [Fact]
    public async Task DismissTask_WritesAuditEvent()
    {
        var entity = new PatternMaintenanceTaskEntity
        {
            Id = Guid.NewGuid(), TenantId = TenantId, PatternId = "p1",
            TaskType = "Unused", Severity = "Info",
            Description = "Not used", RecommendedAction = "Review",
            MetricsJson = "{}", Status = "Pending",
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedAtEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };
        _db.PatternMaintenanceTasks.Add(entity);
        _db.SaveChanges();

        await _service.DismissTaskAsync(
            entity.Id, TenantId, ActorId, CorrelationId,
            new ResolveMaintenanceTaskRequest());

        Assert.Contains(_auditWriter.Events,
            e => e.EventType == AuditEventTypes.MaintenanceTaskDismissed);
    }

    // --- ExtractPatternIds ---

    [Fact]
    public void ExtractPatternIds_ExtractsPatternPrefixed()
    {
        var ids = _service.ExtractPatternIds(
            "[\"ev-123\",\"pattern-abc\",\"chunk-456\",\"pattern-def\"]");
        Assert.Equal(2, ids.Count);
        Assert.Contains("pattern-abc", ids);
        Assert.Contains("pattern-def", ids);
    }

    [Fact]
    public void ExtractPatternIds_Empty_ReturnsEmpty()
    {
        Assert.Empty(_service.ExtractPatternIds(""));
        Assert.Empty(_service.ExtractPatternIds("[]"));
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
