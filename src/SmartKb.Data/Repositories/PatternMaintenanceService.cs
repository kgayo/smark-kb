using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data.Entities;

namespace SmartKb.Data.Repositories;

/// <summary>
/// P2-004: Automated pattern maintenance — detects stale, low-quality, and unused patterns.
/// All detected issues are recorded as tasks requiring human review before any action.
/// </summary>
public sealed class PatternMaintenanceService : IPatternMaintenanceService
{
    private readonly SmartKbDbContext _db;
    private readonly IAuditEventWriter _auditWriter;
    private readonly PatternMaintenanceSettings _settings;
    private readonly ILogger<PatternMaintenanceService> _logger;

    public PatternMaintenanceService(
        SmartKbDbContext db,
        IAuditEventWriter auditWriter,
        PatternMaintenanceSettings settings,
        ILogger<PatternMaintenanceService> logger)
    {
        _db = db;
        _auditWriter = auditWriter;
        _settings = settings;
        _logger = logger;
    }

    public async Task<MaintenanceDetectionResult> DetectMaintenanceIssuesAsync(
        string tenantId, string actorId, string correlationId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        // Load active patterns.
        var patterns = await _db.CasePatterns
            .Where(p => p.TenantId == tenantId && p.TrustLevel != TrustLevelName.Deprecated)
            .ToListAsync(ct);

        // Load existing pending tasks to avoid duplicates.
        var existingTasks = (await _db.PatternMaintenanceTasks
            .Where(t => t.TenantId == tenantId && t.Status == WorkflowStatus.Pending)
            .ToListAsync(ct))
            .Select(t => $"{t.PatternId}|{t.TaskType}")
            .ToHashSet();

        // Load recent answer traces to determine pattern usage.
        var usageCutoff = now.AddDays(-_settings.UnusedDaysThreshold);
        var recentTraces = await _db.Set<AnswerTraceEntity>()
            .Where(a => a.TenantId == tenantId)
            .ToListAsync(ct);
        var recentUsedPatternIds = recentTraces
            .Where(a => a.CreatedAt >= usageCutoff)
            .SelectMany(a => ExtractPatternIds(a.CitedChunkIds))
            .ToHashSet();

        int staleDetected = 0;
        int lowQualityDetected = 0;
        int unusedDetected = 0;
        var newTasks = new List<PatternMaintenanceTaskEntity>();

        foreach (var pattern in patterns)
        {
            // Stale detection: pattern not updated in StaleDaysThreshold days.
            var staleCutoff = now.AddDays(-_settings.StaleDaysThreshold);
            if (pattern.UpdatedAt < staleCutoff)
            {
                var key = $"{pattern.PatternId}|Stale";
                if (!existingTasks.Contains(key))
                {
                    var daysSinceUpdate = (int)(now - pattern.UpdatedAt).TotalDays;
                    newTasks.Add(new PatternMaintenanceTaskEntity
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId,
                        PatternId = pattern.PatternId,
                        TaskType = "Stale",
                        Severity = daysSinceUpdate > _settings.StaleDaysThreshold * 2 ? "Critical" : "Warning",
                        Description = $"Pattern has not been updated in {daysSinceUpdate} days (threshold: {_settings.StaleDaysThreshold}).",
                        RecommendedAction = "Review pattern for accuracy and relevance. Update, deprecate, or confirm still valid.",
                        MetricsJson = JsonSerializer.Serialize(new Dictionary<string, object>
                        {
                            ["daysSinceUpdate"] = daysSinceUpdate,
                            ["lastUpdated"] = pattern.UpdatedAt.ToString("O"),
                            ["trustLevel"] = pattern.TrustLevel,
                        }, SharedJsonOptions.CamelCaseWrite),
                        Status = WorkflowStatus.Pending,
                        CreatedAt = now,
                    });
                    existingTasks.Add(key);
                    staleDetected++;
                }
            }

            // Low quality detection.
            if (pattern.QualityScore.HasValue && pattern.QualityScore.Value < _settings.LowQualityThreshold)
            {
                var key = $"{pattern.PatternId}|LowQuality";
                if (!existingTasks.Contains(key))
                {
                    newTasks.Add(new PatternMaintenanceTaskEntity
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId,
                        PatternId = pattern.PatternId,
                        TaskType = "LowQuality",
                        Severity = pattern.QualityScore.Value < 0.2f ? "Critical" : "Warning",
                        Description = $"Pattern quality score ({pattern.QualityScore.Value:F2}) is below threshold ({_settings.LowQualityThreshold:F2}).",
                        RecommendedAction = "Improve pattern content (symptoms, resolution steps, problem statement) or deprecate.",
                        MetricsJson = JsonSerializer.Serialize(new Dictionary<string, object>
                        {
                            ["qualityScore"] = pattern.QualityScore.Value,
                            ["threshold"] = _settings.LowQualityThreshold,
                            ["trustLevel"] = pattern.TrustLevel,
                        }, SharedJsonOptions.CamelCaseWrite),
                        Status = WorkflowStatus.Pending,
                        CreatedAt = now,
                    });
                    existingTasks.Add(key);
                    lowQualityDetected++;
                }
            }

            // Unused detection: not cited in any answer trace within threshold.
            if (!recentUsedPatternIds.Contains(pattern.PatternId))
            {
                var key = $"{pattern.PatternId}|Unused";
                if (!existingTasks.Contains(key))
                {
                    newTasks.Add(new PatternMaintenanceTaskEntity
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId,
                        PatternId = pattern.PatternId,
                        TaskType = "Unused",
                        Severity = "Info",
                        Description = $"Pattern has not been cited in any answer within the last {_settings.UnusedDaysThreshold} days.",
                        RecommendedAction = "Review pattern relevance. May indicate the underlying issue is resolved or the pattern needs better indexing.",
                        MetricsJson = JsonSerializer.Serialize(new Dictionary<string, object>
                        {
                            ["unusedDaysThreshold"] = _settings.UnusedDaysThreshold,
                            ["trustLevel"] = pattern.TrustLevel,
                            ["createdAt"] = pattern.CreatedAt.ToString("O"),
                        }, SharedJsonOptions.CamelCaseWrite),
                        Status = WorkflowStatus.Pending,
                        CreatedAt = now,
                    });
                    existingTasks.Add(key);
                    unusedDetected++;
                }
            }
        }

        if (newTasks.Count > 0)
        {
            _db.PatternMaintenanceTasks.AddRange(newTasks);
            await _db.SaveChangesAsync(ct);
        }

        await _auditWriter.WriteAsync(new AuditEvent(
            EventId: correlationId,
            EventType: AuditEventTypes.MaintenanceDetectionRun,
            TenantId: tenantId,
            ActorId: actorId,
            CorrelationId: correlationId,
            Timestamp: now,
            Detail: $"Maintenance scan: {patterns.Count} patterns scanned, {newTasks.Count} tasks created " +
                    $"(stale={staleDetected}, low_quality={lowQualityDetected}, unused={unusedDetected})"));

        return new MaintenanceDetectionResult
        {
            PatternsScanned = patterns.Count,
            TasksCreated = newTasks.Count,
            StaleDetected = staleDetected,
            LowQualityDetected = lowQualityDetected,
            UnusedDetected = unusedDetected,
            DetectedAt = now,
        };
    }

    public async Task<MaintenanceTaskListResponse> GetMaintenanceTasksAsync(
        string tenantId, string? status, string? taskType, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.PatternMaintenanceTasks
            .Where(t => t.TenantId == tenantId);

        if (!string.IsNullOrEmpty(status))
            query = query.Where(t => t.Status == status);
        if (!string.IsNullOrEmpty(taskType))
            query = query.Where(t => t.TaskType == taskType);

        var totalCount = await query.CountAsync(ct);

        var allEntities = await query.ToListAsync(ct);
        var entities = allEntities
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        // Resolve pattern titles.
        var patternIds = entities.Select(t => t.PatternId).Distinct().ToList();
        var patternTitles = await _db.CasePatterns
            .Where(p => p.TenantId == tenantId && patternIds.Contains(p.PatternId))
            .Select(p => new { p.PatternId, p.Title })
            .ToListAsync(ct);
        var titleMap = patternTitles.ToDictionary(p => p.PatternId, p => p.Title);

        var summaries = entities.Select(t => new MaintenanceTaskSummary
        {
            Id = t.Id,
            PatternId = t.PatternId,
            PatternTitle = titleMap.GetValueOrDefault(t.PatternId, "(unknown)"),
            TaskType = t.TaskType,
            Severity = t.Severity,
            Description = t.Description,
            RecommendedAction = t.RecommendedAction,
            Metrics = DeserializeMetrics(t.MetricsJson),
            Status = t.Status,
            ResolvedBy = t.ResolvedBy,
            ResolvedAt = t.ResolvedAt,
            CreatedAt = t.CreatedAt,
        }).ToList();

        return new MaintenanceTaskListResponse
        {
            Tasks = summaries,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            HasMore = (page * pageSize) < totalCount,
        };
    }

    public async Task<MaintenanceTaskSummary?> ResolveTaskAsync(
        Guid taskId, string tenantId, string actorId,
        string correlationId, ResolveMaintenanceTaskRequest request, CancellationToken ct = default)
    {
        return await UpdateTaskStatusAsync(
            taskId, tenantId, actorId, correlationId,
            "Resolved", AuditEventTypes.MaintenanceTaskResolved, request.Notes, ct);
    }

    public async Task<MaintenanceTaskSummary?> DismissTaskAsync(
        Guid taskId, string tenantId, string actorId,
        string correlationId, ResolveMaintenanceTaskRequest request, CancellationToken ct = default)
    {
        return await UpdateTaskStatusAsync(
            taskId, tenantId, actorId, correlationId,
            "Dismissed", AuditEventTypes.MaintenanceTaskDismissed, request.Notes, ct);
    }

    private async Task<MaintenanceTaskSummary?> UpdateTaskStatusAsync(
        Guid taskId, string tenantId, string actorId, string correlationId,
        string newStatus, string auditEventType, string? notes, CancellationToken ct = default)
    {
        var entity = await _db.PatternMaintenanceTasks
            .Where(t => t.Id == taskId && t.TenantId == tenantId)
            .FirstOrDefaultAsync(ct);

        if (entity is null || entity.Status != WorkflowStatus.Pending)
            return null;

        var now = DateTimeOffset.UtcNow;
        entity.Status = newStatus;
        entity.ResolvedBy = actorId;
        entity.ResolvedAt = now;
        entity.ResolutionNotes = notes;

        await _db.SaveChangesAsync(ct);

        await _auditWriter.WriteAsync(new AuditEvent(
            EventId: taskId.ToString(),
            EventType: auditEventType,
            TenantId: tenantId,
            ActorId: actorId,
            CorrelationId: correlationId,
            Timestamp: now,
            Detail: $"Maintenance task {taskId} ({entity.TaskType}) for pattern {entity.PatternId} {newStatus.ToLowerInvariant()}"));

        var title = await _db.CasePatterns
            .Where(p => p.TenantId == tenantId && p.PatternId == entity.PatternId)
            .Select(p => p.Title)
            .FirstOrDefaultAsync(ct) ?? "(unknown)";

        return new MaintenanceTaskSummary
        {
            Id = entity.Id,
            PatternId = entity.PatternId,
            PatternTitle = title,
            TaskType = entity.TaskType,
            Severity = entity.Severity,
            Description = entity.Description,
            RecommendedAction = entity.RecommendedAction,
            Metrics = DeserializeMetrics(entity.MetricsJson),
            Status = entity.Status,
            ResolvedBy = entity.ResolvedBy,
            ResolvedAt = entity.ResolvedAt,
            CreatedAt = entity.CreatedAt,
        };
    }

    internal HashSet<string> ExtractPatternIds(string citedChunkIdsJson)
        => PatternIdHelper.ExtractPatternIds(citedChunkIdsJson, _logger);

    private IReadOnlyDictionary<string, object> DeserializeMetrics(string json)
    {
        var dict = JsonDeserializeHelper.DeserializeOrNull<Dictionary<string, JsonElement>>(json, SharedJsonOptions.CamelCaseWrite, _logger);
        if (dict is null) return new Dictionary<string, object>();
        return dict.ToDictionary(kv => kv.Key, kv => (object)kv.Value.ToString()!);
    }
}
