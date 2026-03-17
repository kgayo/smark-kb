using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data.Entities;

namespace SmartKb.Data.Repositories;

public sealed class OutcomeService : IOutcomeService
{
    private readonly SmartKbDbContext _db;
    private readonly IAuditEventWriter _auditWriter;
    private readonly ILogger<OutcomeService> _logger;

    public OutcomeService(
        SmartKbDbContext db,
        IAuditEventWriter auditWriter,
        ILogger<OutcomeService> logger)
    {
        _db = db;
        _auditWriter = auditWriter;
        _logger = logger;
    }

    public async Task<OutcomeResponse> RecordOutcomeAsync(
        string tenantId, string userId, string correlationId,
        Guid sessionId,
        RecordOutcomeRequest request, CancellationToken ct = default)
    {
        var session = await _db.Sessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.TenantId == tenantId && s.UserId == userId, ct);

        if (session is null)
            throw new InvalidOperationException("Session not found or not owned by current user.");

        var now = DateTimeOffset.UtcNow;

        var outcome = new OutcomeEventEntity
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            TenantId = tenantId,
            ResolutionType = request.ResolutionType,
            TargetTeam = request.TargetTeam,
            Acceptance = request.Acceptance,
            TimeToAssign = request.TimeToAssign,
            TimeToResolve = request.TimeToResolve,
            EscalationTraceId = request.EscalationTraceId,
            CreatedAt = now,
        };

        _db.OutcomeEvents.Add(outcome);
        await _db.SaveChangesAsync(ct);

        await _auditWriter.WriteAsync(new AuditEvent(
            EventId: outcome.Id.ToString(),
            EventType: AuditEventTypes.ChatOutcome,
            TenantId: tenantId,
            ActorId: userId,
            CorrelationId: correlationId,
            Timestamp: now,
            Detail: $"Outcome recorded: {request.ResolutionType} for session {sessionId}"), ct);

        _logger.LogInformation(
            "Outcome recorded. OutcomeId={OutcomeId}, SessionId={SessionId}, ResolutionType={ResolutionType}, TenantId={TenantId}",
            outcome.Id, sessionId, request.ResolutionType, tenantId);

        return MapOutcome(outcome);
    }

    public async Task<OutcomeListResponse?> GetOutcomesAsync(
        string tenantId, string userId,
        Guid sessionId, CancellationToken ct = default)
    {
        var sessionExists = await _db.Sessions
            .AnyAsync(s => s.Id == sessionId && s.TenantId == tenantId && s.UserId == userId, ct);

        if (!sessionExists) return null;

        var outcomes = await _db.OutcomeEvents
            .Where(o => o.SessionId == sessionId && o.TenantId == tenantId)
            .ToListAsync(ct);

        outcomes = outcomes.OrderByDescending(o => o.CreatedAt).ToList();

        return new OutcomeListResponse
        {
            SessionId = sessionId,
            Outcomes = outcomes.Select(MapOutcome).ToList(),
            TotalCount = outcomes.Count,
        };
    }

    private static OutcomeResponse MapOutcome(OutcomeEventEntity entity) => new()
    {
        OutcomeId = entity.Id,
        SessionId = entity.SessionId,
        ResolutionType = entity.ResolutionType.ToString(),
        TargetTeam = entity.TargetTeam,
        Acceptance = entity.Acceptance,
        TimeToAssign = entity.TimeToAssign?.ToString(),
        TimeToResolve = entity.TimeToResolve?.ToString(),
        EscalationTraceId = entity.EscalationTraceId,
        CreatedAt = entity.CreatedAt,
    };
}
