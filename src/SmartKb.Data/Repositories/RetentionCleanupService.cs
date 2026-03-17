using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data.Entities;

namespace SmartKb.Data.Repositories;

public sealed class RetentionCleanupService : IRetentionCleanupService
{
    private static readonly string[] ValidEntityTypes =
        ["AppSession", "Message", "AuditEvent", "EvidenceChunk", "AnswerTrace"];

    private readonly SmartKbDbContext _db;
    private readonly IAuditEventWriter _auditWriter;
    private readonly ILogger<RetentionCleanupService> _logger;

    public RetentionCleanupService(
        SmartKbDbContext db,
        IAuditEventWriter auditWriter,
        ILogger<RetentionCleanupService> logger)
    {
        _db = db;
        _auditWriter = auditWriter;
        _logger = logger;
    }

    public async Task<RetentionPolicyResponse> GetPoliciesAsync(string tenantId, CancellationToken ct = default)
    {
        var configs = await _db.RetentionConfigs
            .Where(r => r.TenantId == tenantId)
            .OrderBy(r => r.EntityType)
            .ToListAsync(ct);

        return new RetentionPolicyResponse
        {
            TenantId = tenantId,
            Policies = configs.Select(c => new RetentionPolicyEntry
            {
                EntityType = c.EntityType,
                RetentionDays = c.RetentionDays,
                UpdatedAt = c.UpdatedAt,
            }).ToList(),
        };
    }

    public async Task<RetentionPolicyEntry> UpsertPolicyAsync(
        string tenantId, RetentionPolicyUpdateRequest request, string actorId, CancellationToken ct = default)
    {
        if (!ValidEntityTypes.Contains(request.EntityType))
            throw new ArgumentException($"Invalid entity type: {request.EntityType}. Must be one of: {string.Join(", ", ValidEntityTypes)}");

        if (request.RetentionDays < 1)
            throw new ArgumentException("RetentionDays must be at least 1.");

        var now = DateTimeOffset.UtcNow;
        var entity = await _db.RetentionConfigs
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.EntityType == request.EntityType, ct);

        if (entity is null)
        {
            entity = new RetentionConfigEntity
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                EntityType = request.EntityType,
                CreatedAt = now,
            };
            _db.RetentionConfigs.Add(entity);
        }

        entity.RetentionDays = request.RetentionDays;
        entity.UpdatedAt = now;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Retention policy updated. TenantId={TenantId} EntityType={EntityType} Days={Days}",
            tenantId, request.EntityType, request.RetentionDays);

        await _auditWriter.WriteAsync(new AuditEvent(
            EventId: Guid.NewGuid().ToString(),
            EventType: AuditEventTypes.RetentionPolicyUpdated,
            TenantId: tenantId,
            ActorId: actorId,
            CorrelationId: Guid.NewGuid().ToString(),
            Timestamp: now,
            Detail: $"Retention policy set: {request.EntityType}={request.RetentionDays} days"), ct);

        return new RetentionPolicyEntry
        {
            EntityType = entity.EntityType,
            RetentionDays = entity.RetentionDays,
            UpdatedAt = entity.UpdatedAt,
        };
    }

    public async Task<bool> DeletePolicyAsync(
        string tenantId, string entityType, string actorId, CancellationToken ct = default)
    {
        var entity = await _db.RetentionConfigs
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.EntityType == entityType, ct);

        if (entity is null) return false;

        _db.RetentionConfigs.Remove(entity);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Retention policy deleted. TenantId={TenantId} EntityType={EntityType}",
            tenantId, entityType);

        await _auditWriter.WriteAsync(new AuditEvent(
            EventId: Guid.NewGuid().ToString(),
            EventType: AuditEventTypes.RetentionPolicyUpdated,
            TenantId: tenantId,
            ActorId: actorId,
            CorrelationId: Guid.NewGuid().ToString(),
            Timestamp: DateTimeOffset.UtcNow,
            Detail: $"Retention policy deleted for {entityType}"), ct);

        return true;
    }

    public async Task<IReadOnlyList<RetentionCleanupResult>> ExecuteCleanupAsync(
        string tenantId, CancellationToken ct = default)
    {
        var configs = await _db.RetentionConfigs
            .Where(r => r.TenantId == tenantId)
            .ToListAsync(ct);

        if (configs.Count == 0) return [];

        var results = new List<RetentionCleanupResult>();
        var now = DateTimeOffset.UtcNow;

        foreach (var config in configs)
        {
            var cutoff = now.AddDays(-config.RetentionDays);
            var deleted = await ExecuteEntityCleanup(tenantId, config.EntityType, cutoff, ct);

            if (deleted > 0)
            {
                Diagnostics.RetentionCleanupDeletedTotal.Add(deleted,
                    new System.Diagnostics.TagList
                    {
                        { "tenant_id", tenantId },
                        { "entity_type", config.EntityType },
                    });
            }

            results.Add(new RetentionCleanupResult
            {
                TenantId = tenantId,
                EntityType = config.EntityType,
                DeletedCount = deleted,
                CutoffDate = cutoff,
                ExecutedAt = now,
            });
        }

        var totalDeleted = results.Sum(r => r.DeletedCount);
        if (totalDeleted > 0)
        {
            var summary = string.Join(", ", results.Where(r => r.DeletedCount > 0)
                .Select(r => $"{r.EntityType}={r.DeletedCount}"));

            _logger.LogInformation(
                "Retention cleanup completed. TenantId={TenantId} Deleted={Summary}",
                tenantId, summary);

            await _auditWriter.WriteAsync(new AuditEvent(
                EventId: Guid.NewGuid().ToString(),
                EventType: AuditEventTypes.RetentionCleanupExecuted,
                TenantId: tenantId,
                ActorId: "system",
                CorrelationId: Guid.NewGuid().ToString(),
                Timestamp: now,
                Detail: $"Retention cleanup: {summary}"), ct);
        }

        return results;
    }

    private async Task<int> ExecuteEntityCleanup(
        string tenantId, string entityType, DateTimeOffset cutoff, CancellationToken ct)
    {
        return entityType switch
        {
            "AppSession" => await CleanupSessions(tenantId, cutoff, ct),
            "Message" => await CleanupMessages(tenantId, cutoff, ct),
            "AuditEvent" => await CleanupAuditEvents(tenantId, cutoff, ct),
            "EvidenceChunk" => await CleanupEvidenceChunks(tenantId, cutoff, ct),
            "AnswerTrace" => await CleanupAnswerTraces(tenantId, cutoff, ct),
            _ => 0,
        };
    }

    private async Task<int> CleanupSessions(string tenantId, DateTimeOffset cutoff, CancellationToken ct)
    {
        // Load tenant sessions, then filter by date client-side to avoid DateTimeOffset
        // translation issues across database providers.
        var sessions = (await _db.Sessions
            .Where(s => s.TenantId == tenantId)
            .ToListAsync(ct))
            .Where(s => s.CreatedAt < cutoff)
            .ToList();

        foreach (var session in sessions)
            session.DeletedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
        return sessions.Count;
    }

    private async Task<int> CleanupMessages(string tenantId, DateTimeOffset cutoff, CancellationToken ct)
    {
        var messages = (await _db.Messages
            .Where(m => m.TenantId == tenantId)
            .ToListAsync(ct))
            .Where(m => m.CreatedAt < cutoff)
            .ToList();

        foreach (var msg in messages)
            msg.DeletedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
        return messages.Count;
    }

    private async Task<int> CleanupAuditEvents(string tenantId, DateTimeOffset cutoff, CancellationToken ct)
    {
        var events = (await _db.AuditEvents
            .Where(a => a.TenantId == tenantId)
            .ToListAsync(ct))
            .Where(a => a.Timestamp < cutoff)
            .ToList();

        _db.AuditEvents.RemoveRange(events);
        await _db.SaveChangesAsync(ct);
        return events.Count;
    }

    private async Task<int> CleanupEvidenceChunks(string tenantId, DateTimeOffset cutoff, CancellationToken ct)
    {
        var chunks = (await _db.EvidenceChunks
            .Where(c => c.TenantId == tenantId)
            .ToListAsync(ct))
            .Where(c => c.CreatedAt < cutoff)
            .ToList();

        _db.EvidenceChunks.RemoveRange(chunks);
        await _db.SaveChangesAsync(ct);
        return chunks.Count;
    }

    private async Task<int> CleanupAnswerTraces(string tenantId, DateTimeOffset cutoff, CancellationToken ct)
    {
        var traces = (await _db.AnswerTraces
            .Where(a => a.TenantId == tenantId)
            .ToListAsync(ct))
            .Where(a => a.CreatedAt < cutoff)
            .ToList();

        _db.AnswerTraces.RemoveRange(traces);
        await _db.SaveChangesAsync(ct);
        return traces.Count;
    }
}
