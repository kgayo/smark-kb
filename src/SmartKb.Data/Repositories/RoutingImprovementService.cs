using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data.Entities;

namespace SmartKb.Data.Repositories;

public sealed class RoutingImprovementService : IRoutingImprovementService
{
    private readonly SmartKbDbContext _db;
    private readonly IRoutingAnalyticsService _analytics;
    private readonly IAuditEventWriter _auditWriter;
    private readonly RoutingAnalyticsSettings _settings;
    private readonly ILogger<RoutingImprovementService> _logger;

    public RoutingImprovementService(
        SmartKbDbContext db,
        IRoutingAnalyticsService analytics,
        IAuditEventWriter auditWriter,
        RoutingAnalyticsSettings settings,
        ILogger<RoutingImprovementService> logger)
    {
        _db = db;
        _analytics = analytics;
        _auditWriter = auditWriter;
        _settings = settings;
        _logger = logger;
    }

    public async Task<RoutingRecommendationListResponse> GenerateRecommendationsAsync(
        string tenantId, string userId, string correlationId,
        CancellationToken ct = default)
    {
        var summary = await _analytics.GetAnalyticsAsync(tenantId, ct: ct);
        var generated = new List<RoutingRecommendationEntity>();
        var now = DateTimeOffset.UtcNow;

        foreach (var area in summary.ProductAreaMetrics)
        {
            if (area.TotalEscalations < _settings.MinOutcomesForRecommendation)
                continue;

            // Check for existing pending recommendation for this product area.
            var existingPending = await _db.RoutingRecommendations
                .AnyAsync(r => r.TenantId == tenantId
                               && r.ProductArea == area.ProductArea
                               && r.Status == "Pending", ct);
            if (existingPending) continue;

            // High reroute rate → suggest team change.
            if (area.RerouteRate >= _settings.RerouteRateThreshold)
            {
                var suggestedTeam = await FindBetterTeamAsync(tenantId, area, ct);

                var confidence = ComputeConfidence(area.TotalEscalations, area.RerouteRate);
                if (confidence < _settings.MinRecommendationConfidence)
                    continue;

                var recommendation = new RoutingRecommendationEntity
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    RecommendationType = "TeamChange",
                    ProductArea = area.ProductArea,
                    CurrentTargetTeam = area.CurrentTargetTeam,
                    SuggestedTargetTeam = suggestedTeam,
                    Reason = $"Reroute rate {area.RerouteRate:P0} exceeds threshold {_settings.RerouteRateThreshold:P0} " +
                             $"({area.ReroutedCount}/{area.TotalEscalations} escalations rerouted).",
                    Confidence = confidence,
                    SupportingOutcomeCount = area.TotalEscalations,
                    Status = "Pending",
                    CreatedAt = now,
                };
                _db.RoutingRecommendations.Add(recommendation);
                generated.Add(recommendation);
            }
            // Low acceptance rate → suggest threshold adjustment.
            else if (area.AcceptanceRate < _settings.LowAcceptanceRateThreshold && area.AcceptanceRate > 0)
            {
                var currentRule = await _db.EscalationRoutingRules
                    .FirstOrDefaultAsync(r => r.TenantId == tenantId
                                              && r.ProductArea == area.ProductArea
                                              && r.IsActive, ct);

                var currentThreshold = currentRule?.EscalationThreshold ?? 0.4f;
                var suggestedThreshold = Math.Max(0.1f, currentThreshold - 0.1f);

                var confidence = ComputeConfidence(area.TotalEscalations, 1f - area.AcceptanceRate);
                if (confidence < _settings.MinRecommendationConfidence)
                    continue;

                var recommendation = new RoutingRecommendationEntity
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    RecommendationType = "ThresholdAdjust",
                    ProductArea = area.ProductArea,
                    CurrentTargetTeam = area.CurrentTargetTeam,
                    CurrentThreshold = currentThreshold,
                    SuggestedThreshold = suggestedThreshold,
                    Reason = $"Acceptance rate {area.AcceptanceRate:P0} below threshold {_settings.LowAcceptanceRateThreshold:P0} " +
                             $"({area.AcceptedCount}/{area.TotalEscalations} accepted). " +
                             $"Consider lowering escalation threshold from {currentThreshold:F2} to {suggestedThreshold:F2}.",
                    Confidence = confidence,
                    SupportingOutcomeCount = area.TotalEscalations,
                    Status = "Pending",
                    CreatedAt = now,
                };
                _db.RoutingRecommendations.Add(recommendation);
                generated.Add(recommendation);
            }
        }

        if (generated.Count > 0)
        {
            await _db.SaveChangesAsync(ct);

            foreach (var rec in generated)
            {
                await _auditWriter.WriteAsync(new AuditEvent(
                    EventId: rec.Id.ToString(),
                    EventType: AuditEventTypes.RoutingRecommendationGenerated,
                    TenantId: tenantId,
                    ActorId: userId,
                    CorrelationId: correlationId,
                    Timestamp: now,
                    Detail: $"Routing recommendation generated: {rec.RecommendationType} for {rec.ProductArea}"), ct);
            }

            _logger.LogInformation(
                "Generated {Count} routing recommendations for TenantId={TenantId}",
                generated.Count, tenantId);
        }

        return new RoutingRecommendationListResponse
        {
            Recommendations = generated.Select(MapRecommendation).ToList(),
            TotalCount = generated.Count,
        };
    }

    public async Task<RoutingRecommendationListResponse> GetRecommendationsAsync(
        string tenantId, string? status = null, CancellationToken ct = default)
    {
        var query = _db.RoutingRecommendations
            .Where(r => r.TenantId == tenantId);

        if (!string.IsNullOrEmpty(status))
            query = query.Where(r => r.Status == status);

        var results = (await query.ToListAsync(ct)).OrderByDescending(r => r.CreatedAt).ToList();

        return new RoutingRecommendationListResponse
        {
            Recommendations = results.Select(MapRecommendation).ToList(),
            TotalCount = results.Count,
        };
    }

    public async Task<RoutingRecommendationDto?> ApplyRecommendationAsync(
        string tenantId, string userId, string correlationId,
        Guid recommendationId,
        ApplyRecommendationRequest? overrides = null,
        CancellationToken ct = default)
    {
        var rec = await _db.RoutingRecommendations
            .FirstOrDefaultAsync(r => r.Id == recommendationId && r.TenantId == tenantId, ct);

        if (rec is null || rec.Status != "Pending") return null;

        var now = DateTimeOffset.UtcNow;

        // Apply the recommendation to the routing rule.
        var rule = await _db.EscalationRoutingRules
            .FirstOrDefaultAsync(r => r.TenantId == tenantId
                                      && r.ProductArea == rec.ProductArea
                                      && r.IsActive, ct);

        if (rec.RecommendationType == "TeamChange")
        {
            var newTeam = overrides?.OverrideTargetTeam ?? rec.SuggestedTargetTeam ?? rec.CurrentTargetTeam;
            if (rule is not null)
            {
                rule.TargetTeam = newTeam;
                rule.UpdatedAt = now;
            }
            else
            {
                _db.EscalationRoutingRules.Add(new EscalationRoutingRuleEntity
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    ProductArea = rec.ProductArea,
                    TargetTeam = newTeam,
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }
        }
        else if (rec.RecommendationType == "ThresholdAdjust")
        {
            var newThreshold = overrides?.OverrideThreshold ?? rec.SuggestedThreshold ?? 0.3f;
            if (rule is not null)
            {
                rule.EscalationThreshold = newThreshold;
                rule.UpdatedAt = now;
            }
            else
            {
                _db.EscalationRoutingRules.Add(new EscalationRoutingRuleEntity
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    ProductArea = rec.ProductArea,
                    TargetTeam = rec.CurrentTargetTeam,
                    EscalationThreshold = newThreshold,
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }
        }

        rec.Status = "Applied";
        rec.AppliedAt = now;
        rec.AppliedBy = userId;

        await _db.SaveChangesAsync(ct);

        await _auditWriter.WriteAsync(new AuditEvent(
            EventId: rec.Id.ToString(),
            EventType: AuditEventTypes.RoutingRecommendationApplied,
            TenantId: tenantId,
            ActorId: userId,
            CorrelationId: correlationId,
            Timestamp: now,
            Detail: $"Routing recommendation applied: {rec.RecommendationType} for {rec.ProductArea}"), ct);

        _logger.LogInformation(
            "Routing recommendation applied. RecommendationId={RecommendationId}, Type={Type}, ProductArea={ProductArea}, TenantId={TenantId}",
            rec.Id, rec.RecommendationType, rec.ProductArea, tenantId);

        return MapRecommendation(rec);
    }

    public async Task<bool> DismissRecommendationAsync(
        string tenantId, string userId, string correlationId,
        Guid recommendationId, CancellationToken ct = default)
    {
        var rec = await _db.RoutingRecommendations
            .FirstOrDefaultAsync(r => r.Id == recommendationId && r.TenantId == tenantId, ct);

        if (rec is null || rec.Status != "Pending") return false;

        rec.Status = "Dismissed";
        rec.DismissedAt = DateTimeOffset.UtcNow;
        rec.DismissedBy = userId;

        await _db.SaveChangesAsync(ct);

        await _auditWriter.WriteAsync(new AuditEvent(
            EventId: rec.Id.ToString(),
            EventType: AuditEventTypes.RoutingRecommendationDismissed,
            TenantId: tenantId,
            ActorId: userId,
            CorrelationId: correlationId,
            Timestamp: DateTimeOffset.UtcNow,
            Detail: $"Routing recommendation dismissed: {rec.RecommendationType} for {rec.ProductArea}"), ct);

        return true;
    }

    private async Task<string?> FindBetterTeamAsync(
        string tenantId, ProductAreaRoutingMetrics area, CancellationToken ct)
    {
        // Look at rerouted outcomes to see which teams they were rerouted TO.
        // The most common reroute target is a likely better team.
        var reroutedOutcomes = await _db.OutcomeEvents
            .Where(o => o.TenantId == tenantId
                        && o.ResolutionType == ResolutionType.Rerouted
                        && o.TargetTeam != null
                        && o.TargetTeam != area.CurrentTargetTeam)
            .ToListAsync(ct);

        // Filter to outcomes linked to this product area via escalation trace.
        var traceIds = reroutedOutcomes
            .Where(o => !string.IsNullOrEmpty(o.EscalationTraceId))
            .Select(o => o.EscalationTraceId!)
            .Distinct()
            .ToList();

        if (traceIds.Count == 0) return null;

        var traceGuids = traceIds
            .Select(t => Guid.TryParse(t, out var g) ? g : (Guid?)null)
            .Where(g => g.HasValue)
            .Select(g => g!.Value)
            .ToList();

        var matchingDraftIds = (await _db.EscalationDrafts
            .IgnoreQueryFilters()
            .Where(d => d.TenantId == tenantId
                        && d.SuspectedComponent == area.ProductArea
                        && traceGuids.Contains(d.Id))
            .Select(d => d.Id)
            .ToListAsync(ct))
            .Select(id => id.ToString())
            .ToList();

        if (matchingDraftIds.Count == 0) return null;

        // Find the most common target team among rerouted outcomes for this area.
        var bestTeam = reroutedOutcomes
            .Where(o => !string.IsNullOrEmpty(o.EscalationTraceId) && matchingDraftIds.Contains(o.EscalationTraceId!))
            .GroupBy(o => o.TargetTeam!)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault()?.Key;

        return bestTeam;
    }

    internal static float ComputeConfidence(int outcomeCount, float signalStrength)
    {
        // Confidence increases with more data and stronger signal.
        // Volume factor: log2(count)/log2(50) capped at 1.0 (50+ outcomes → max volume confidence).
        var volumeFactor = Math.Min(1.0f, (float)(Math.Log2(Math.Max(1, outcomeCount)) / Math.Log2(50)));
        // Signal factor: how strong the reroute/rejection signal is (0-1).
        var confidence = 0.4f * volumeFactor + 0.6f * signalStrength;
        return Math.Clamp(confidence, 0f, 1f);
    }

    private static RoutingRecommendationDto MapRecommendation(RoutingRecommendationEntity entity) => new()
    {
        RecommendationId = entity.Id,
        RecommendationType = entity.RecommendationType,
        ProductArea = entity.ProductArea,
        CurrentTargetTeam = entity.CurrentTargetTeam,
        SuggestedTargetTeam = entity.SuggestedTargetTeam,
        CurrentThreshold = entity.CurrentThreshold,
        SuggestedThreshold = entity.SuggestedThreshold,
        Reason = entity.Reason,
        Confidence = entity.Confidence,
        SupportingOutcomeCount = entity.SupportingOutcomeCount,
        Status = entity.Status,
        CreatedAt = entity.CreatedAt,
        AppliedAt = entity.AppliedAt,
        AppliedBy = entity.AppliedBy,
    };
}
