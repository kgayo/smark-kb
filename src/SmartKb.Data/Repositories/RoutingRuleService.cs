using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data.Entities;

namespace SmartKb.Data.Repositories;

public sealed class RoutingRuleService : IRoutingRuleService
{
    private readonly SmartKbDbContext _db;
    private readonly IAuditEventWriter _auditWriter;
    private readonly ILogger<RoutingRuleService> _logger;

    public RoutingRuleService(
        SmartKbDbContext db,
        IAuditEventWriter auditWriter,
        ILogger<RoutingRuleService> logger)
    {
        _db = db;
        _auditWriter = auditWriter;
        _logger = logger;
    }

    public async Task<RoutingRuleListResponse> GetRulesAsync(string tenantId, CancellationToken ct = default)
    {
        var rules = await _db.EscalationRoutingRules
            .Where(r => r.TenantId == tenantId)
            .OrderBy(r => r.ProductArea)
            .ToListAsync(ct);

        return new RoutingRuleListResponse
        {
            Rules = rules.Select(MapRule).ToList(),
            TotalCount = rules.Count,
        };
    }

    public async Task<RoutingRuleDto?> GetRuleAsync(string tenantId, Guid ruleId, CancellationToken ct = default)
    {
        var rule = await _db.EscalationRoutingRules
            .FirstOrDefaultAsync(r => r.Id == ruleId && r.TenantId == tenantId, ct);

        return rule is null ? null : MapRule(rule);
    }

    public async Task<RoutingRuleDto> CreateRuleAsync(
        string tenantId, string userId, string correlationId,
        CreateRoutingRuleRequest request, CancellationToken ct = default)
    {
        var severity = NormalizeSeverity(request.MinSeverity);
        var now = DateTimeOffset.UtcNow;

        var entity = new EscalationRoutingRuleEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProductArea = request.ProductArea,
            TargetTeam = request.TargetTeam,
            EscalationThreshold = request.EscalationThreshold,
            MinSeverity = severity,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };

        _db.EscalationRoutingRules.Add(entity);
        await _db.SaveChangesAsync(ct);

        await _auditWriter.WriteAsync(new AuditEvent(
            EventId: entity.Id.ToString(),
            EventType: AuditEventTypes.RoutingRuleCreated,
            TenantId: tenantId,
            ActorId: userId,
            CorrelationId: correlationId,
            Timestamp: now,
            Detail: $"Routing rule created: {request.ProductArea} → {request.TargetTeam}"), ct);

        _logger.LogInformation(
            "Routing rule created. RuleId={RuleId}, ProductArea={ProductArea}, TargetTeam={TargetTeam}, TenantId={TenantId}",
            entity.Id, request.ProductArea, request.TargetTeam, tenantId);

        return MapRule(entity);
    }

    public async Task<RoutingRuleDto?> UpdateRuleAsync(
        string tenantId, string userId, string correlationId,
        Guid ruleId, UpdateRoutingRuleRequest request, CancellationToken ct = default)
    {
        var entity = await _db.EscalationRoutingRules
            .FirstOrDefaultAsync(r => r.Id == ruleId && r.TenantId == tenantId, ct);

        if (entity is null) return null;

        var now = DateTimeOffset.UtcNow;

        if (request.TargetTeam is not null) entity.TargetTeam = request.TargetTeam;
        if (request.EscalationThreshold.HasValue) entity.EscalationThreshold = request.EscalationThreshold.Value;
        if (request.MinSeverity is not null) entity.MinSeverity = NormalizeSeverity(request.MinSeverity);
        if (request.IsActive.HasValue) entity.IsActive = request.IsActive.Value;
        entity.UpdatedAt = now;

        await _db.SaveChangesAsync(ct);

        await _auditWriter.WriteAsync(new AuditEvent(
            EventId: entity.Id.ToString(),
            EventType: AuditEventTypes.RoutingRuleUpdated,
            TenantId: tenantId,
            ActorId: userId,
            CorrelationId: correlationId,
            Timestamp: now,
            Detail: $"Routing rule updated: {entity.ProductArea} → {entity.TargetTeam}"), ct);

        return MapRule(entity);
    }

    public async Task<bool> DeleteRuleAsync(
        string tenantId, string userId, string correlationId,
        Guid ruleId, CancellationToken ct = default)
    {
        var entity = await _db.EscalationRoutingRules
            .FirstOrDefaultAsync(r => r.Id == ruleId && r.TenantId == tenantId, ct);

        if (entity is null) return false;

        _db.EscalationRoutingRules.Remove(entity);
        await _db.SaveChangesAsync(ct);

        await _auditWriter.WriteAsync(new AuditEvent(
            EventId: ruleId.ToString(),
            EventType: AuditEventTypes.RoutingRuleDeleted,
            TenantId: tenantId,
            ActorId: userId,
            CorrelationId: correlationId,
            Timestamp: DateTimeOffset.UtcNow,
            Detail: $"Routing rule deleted: {entity.ProductArea}"), ct);

        _logger.LogInformation(
            "Routing rule deleted. RuleId={RuleId}, ProductArea={ProductArea}, TenantId={TenantId}",
            ruleId, entity.ProductArea, tenantId);

        return true;
    }

    private static RoutingRuleDto MapRule(EscalationRoutingRuleEntity entity) => new()
    {
        RuleId = entity.Id,
        ProductArea = entity.ProductArea,
        TargetTeam = entity.TargetTeam,
        EscalationThreshold = entity.EscalationThreshold,
        MinSeverity = entity.MinSeverity,
        IsActive = entity.IsActive,
        CreatedAt = entity.CreatedAt,
        UpdatedAt = entity.UpdatedAt,
    };

    private static string NormalizeSeverity(string severity)
    {
        var normalized = severity.ToUpperInvariant();
        return EscalationSettings.SeverityOrder.Contains(normalized) ? normalized : "P3";
    }
}
