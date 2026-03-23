using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data.Entities;

namespace SmartKb.Data.Repositories;

public sealed class DataSubjectDeletionService : IDataSubjectDeletionService
{
    private readonly SmartKbDbContext _db;
    private readonly IAuditEventWriter _auditWriter;
    private readonly ILogger<DataSubjectDeletionService> _logger;

    public DataSubjectDeletionService(
        SmartKbDbContext db,
        IAuditEventWriter auditWriter,
        ILogger<DataSubjectDeletionService> logger)
    {
        _db = db;
        _auditWriter = auditWriter;
        _logger = logger;
    }

    public async Task<DataSubjectDeletionResponse> RequestDeletionAsync(
        string tenantId, string subjectId, string requestedBy, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var requestEntity = new DataSubjectDeletionRequestEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SubjectId = subjectId,
            RequestedBy = requestedBy,
            Status = "Processing",
            RequestedAt = now,
        };
        _db.DataSubjectDeletionRequests.Add(requestEntity);
        await _db.SaveChangesAsync(ct);

        await _auditWriter.WriteAsync(new AuditEvent(
            EventId: Guid.NewGuid().ToString(),
            EventType: AuditEventTypes.DataSubjectDeletionRequested,
            TenantId: tenantId,
            ActorId: requestedBy,
            CorrelationId: requestEntity.Id.ToString(),
            Timestamp: now,
            Detail: $"Data subject deletion requested for subject={subjectId}"), ct);

        try
        {
            var summary = await ExecuteDeletion(tenantId, subjectId, ct);

            requestEntity.Status = "Completed";
            requestEntity.CompletedAt = DateTimeOffset.UtcNow;
            requestEntity.DeletionSummaryJson = JsonSerializer.Serialize(summary);
            await _db.SaveChangesAsync(ct);

            var totalDeleted = summary.Values.Sum();
            Diagnostics.DataSubjectDeletionsTotal.Add(1,
                new System.Diagnostics.TagList { { "tenant_id", tenantId } });

            _logger.LogInformation(
                "Data subject deletion completed. TenantId={TenantId} SubjectId={SubjectId} TotalDeleted={Total}",
                tenantId, subjectId, totalDeleted);

            await _auditWriter.WriteAsync(new AuditEvent(
                EventId: Guid.NewGuid().ToString(),
                EventType: AuditEventTypes.DataSubjectDeletionCompleted,
                TenantId: tenantId,
                ActorId: requestedBy,
                CorrelationId: requestEntity.Id.ToString(),
                Timestamp: DateTimeOffset.UtcNow,
                Detail: $"Data subject deletion completed for subject={subjectId}: {JsonSerializer.Serialize(summary)}"), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            requestEntity.Status = "Failed";
            requestEntity.ErrorDetail = ex.Message;
            requestEntity.CompletedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);

            _logger.LogError(ex,
                "Data subject deletion failed. TenantId={TenantId} SubjectId={SubjectId}",
                tenantId, subjectId);

            await _auditWriter.WriteAsync(new AuditEvent(
                EventId: Guid.NewGuid().ToString(),
                EventType: AuditEventTypes.DataSubjectDeletionFailed,
                TenantId: tenantId,
                ActorId: requestedBy,
                CorrelationId: requestEntity.Id.ToString(),
                Timestamp: DateTimeOffset.UtcNow,
                Detail: $"Data subject deletion failed for subject={subjectId}: {ex.Message}"), ct);
        }

        return ToResponse(requestEntity);
    }

    public async Task<DataSubjectDeletionResponse?> GetDeletionRequestAsync(
        string tenantId, Guid requestId, CancellationToken ct = default)
    {
        var entity = await _db.DataSubjectDeletionRequests
            .FirstOrDefaultAsync(d => d.TenantId == tenantId && d.Id == requestId, ct);

        return entity is null ? null : ToResponse(entity);
    }

    public async Task<DataSubjectDeletionListResponse> ListDeletionRequestsAsync(
        string tenantId, CancellationToken ct = default)
    {
        var entities = (await _db.DataSubjectDeletionRequests
            .Where(d => d.TenantId == tenantId)
            .ToListAsync(ct))
            .OrderByDescending(d => d.RequestedAt)
            .ToList();

        return new DataSubjectDeletionListResponse
        {
            Requests = entities.Select(ToResponse).ToList(),
            TotalCount = entities.Count,
        };
    }

    private async Task<Dictionary<string, int>> ExecuteDeletion(
        string tenantId, string subjectId, CancellationToken ct)
    {
        var summary = new Dictionary<string, int>();

        // 1. Soft-delete escalation drafts by this user.
        var drafts = await _db.EscalationDrafts
            .IgnoreQueryFilters()
            .Where(d => d.TenantId == tenantId && d.UserId == subjectId && d.DeletedAt == null)
            .ToListAsync(ct);
        foreach (var d in drafts) d.DeletedAt = DateTimeOffset.UtcNow;
        summary["escalation_drafts"] = drafts.Count;

        // 2. Delete feedback by this user.
        var feedbacks = await _db.Feedbacks
            .Where(f => f.TenantId == tenantId && f.UserId == subjectId)
            .ToListAsync(ct);
        _db.Feedbacks.RemoveRange(feedbacks);
        summary["feedbacks"] = feedbacks.Count;

        // 3. Delete answer traces by this user.
        var traces = await _db.AnswerTraces
            .Where(a => a.TenantId == tenantId && a.UserId == subjectId)
            .ToListAsync(ct);
        _db.AnswerTraces.RemoveRange(traces);
        summary["answer_traces"] = traces.Count;

        // 4. Soft-delete messages in user's sessions.
        var sessionIds = await _db.Sessions
            .IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId && s.UserId == subjectId)
            .Select(s => s.Id)
            .ToListAsync(ct);

        var messages = await _db.Messages
            .IgnoreQueryFilters()
            .Where(m => m.TenantId == tenantId && sessionIds.Contains(m.SessionId) && m.DeletedAt == null)
            .ToListAsync(ct);
        foreach (var m in messages) m.DeletedAt = DateTimeOffset.UtcNow;
        summary["messages"] = messages.Count;

        // 5. Delete outcome events for user's sessions.
        var outcomes = await _db.OutcomeEvents
            .Where(o => o.TenantId == tenantId && sessionIds.Contains(o.SessionId))
            .ToListAsync(ct);
        _db.OutcomeEvents.RemoveRange(outcomes);
        summary["outcome_events"] = outcomes.Count;

        // 6. Soft-delete the sessions themselves.
        var sessions = await _db.Sessions
            .IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId && s.UserId == subjectId && s.DeletedAt == null)
            .ToListAsync(ct);
        foreach (var s in sessions) s.DeletedAt = DateTimeOffset.UtcNow;
        summary["sessions"] = sessions.Count;

        await _db.SaveChangesAsync(ct);

        return summary;
    }

    private static DataSubjectDeletionResponse ToResponse(DataSubjectDeletionRequestEntity entity)
    {
        Dictionary<string, int>? summary = null;
        if (entity.DeletionSummaryJson != "{}")
        {
            try { summary = JsonSerializer.Deserialize<Dictionary<string, int>>(entity.DeletionSummaryJson); }
            catch (JsonException) { /* ignore deserialization failures */ }
        }

        return new DataSubjectDeletionResponse
        {
            RequestId = entity.Id,
            TenantId = entity.TenantId,
            SubjectId = entity.SubjectId,
            Status = entity.Status,
            RequestedAt = entity.RequestedAt,
            CompletedAt = entity.CompletedAt,
            DeletionSummary = summary,
            ErrorDetail = entity.ErrorDetail,
        };
    }
}
