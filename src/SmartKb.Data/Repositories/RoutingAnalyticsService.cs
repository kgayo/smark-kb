using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;

namespace SmartKb.Data.Repositories;

public sealed class RoutingAnalyticsService : IRoutingAnalyticsService
{
    private readonly SmartKbDbContext _db;
    private readonly RoutingAnalyticsSettings _settings;
    private readonly ILogger<RoutingAnalyticsService> _logger;

    public RoutingAnalyticsService(
        SmartKbDbContext db,
        RoutingAnalyticsSettings settings,
        ILogger<RoutingAnalyticsService> logger)
    {
        _db = db;
        _settings = settings;
        _logger = logger;
    }

    public async Task<RoutingAnalyticsSummary> GetAnalyticsAsync(
        string tenantId,
        int? windowDays = null,
        CancellationToken ct = default)
    {
        var days = windowDays ?? _settings.DefaultWindowDays;
        var now = DateTimeOffset.UtcNow;
        var windowStart = now.AddDays(-days);

        var windowStartEpoch = windowStart.ToUnixTimeSeconds();
        var outcomes = await _db.OutcomeEvents
            .Where(o => o.TenantId == tenantId && o.CreatedAtEpoch >= windowStartEpoch)
            .ToListAsync(ct);

        var totalOutcomes = outcomes.Count;
        var escalated = outcomes.Where(o => o.ResolutionType == ResolutionType.Escalated).ToList();
        var rerouted = outcomes.Where(o => o.ResolutionType == ResolutionType.Rerouted).ToList();
        var resolvedWithout = outcomes.Where(o => o.ResolutionType == ResolutionType.ResolvedWithoutEscalation).ToList();

        var totalEscalations = escalated.Count + rerouted.Count;

        // Team-level metrics: group by TargetTeam from escalated + rerouted outcomes.
        var escalationOutcomes = escalated.Concat(rerouted).ToList();
        var teamGroups = escalationOutcomes
            .Where(o => !string.IsNullOrEmpty(o.TargetTeam))
            .GroupBy(o => o.TargetTeam!)
            .ToList();

        var teamMetrics = teamGroups.Select(g =>
        {
            var accepted = g.Count(o => o.Acceptance == true);
            var reroutedInTeam = g.Count(o => o.ResolutionType == ResolutionType.Rerouted);
            var pending = g.Count(o => o.Acceptance == null && o.ResolutionType != ResolutionType.Rerouted);
            var total = g.Count();

            var ttaValues = g.Where(o => o.TimeToAssign.HasValue).Select(o => o.TimeToAssign!.Value).ToList();
            var ttrValues = g.Where(o => o.TimeToResolve.HasValue).Select(o => o.TimeToResolve!.Value).ToList();

            return new TeamRoutingMetrics
            {
                TargetTeam = g.Key,
                TotalEscalations = total,
                AcceptedCount = accepted,
                ReroutedCount = reroutedInTeam,
                PendingCount = pending,
                AcceptanceRate = total > 0 ? (float)accepted / total : 0f,
                RerouteRate = total > 0 ? (float)reroutedInTeam / total : 0f,
                AvgTimeToAssign = ttaValues.Count > 0
                    ? TimeSpan.FromTicks((long)ttaValues.Average(t => t.Ticks))
                    : null,
                AvgTimeToResolve = ttrValues.Count > 0
                    ? TimeSpan.FromTicks((long)ttrValues.Average(t => t.Ticks))
                    : null,
            };
        }).ToList();

        // Product-area metrics: join outcomes with escalation drafts via EscalationTraceId.
        var traceIds = escalationOutcomes
            .Where(o => !string.IsNullOrEmpty(o.EscalationTraceId))
            .Select(o => o.EscalationTraceId!)
            .Distinct()
            .ToList();

        // Load drafts that match trace IDs to get SuspectedComponent.
        // Parse trace IDs to GUIDs for DB-side filtering, then build lookup.
        var traceGuids = traceIds
            .Select(t => Guid.TryParse(t, out var g) ? g : (Guid?)null)
            .Where(g => g.HasValue)
            .Select(g => g!.Value)
            .ToList();

        var drafts = traceGuids.Count > 0
            ? await _db.EscalationDrafts
                .IgnoreQueryFilters()
                .Where(d => d.TenantId == tenantId && traceGuids.Contains(d.Id))
                .Select(d => new { d.Id, d.SuspectedComponent, d.TargetTeam })
                .ToListAsync(ct)
            : [];

        var draftLookup = drafts.ToDictionary(d => d.Id.ToString(), d => d);

        // Also load current routing rules for product area → team mapping.
        var routingRules = await _db.EscalationRoutingRules
            .Where(r => r.TenantId == tenantId && r.IsActive)
            .ToListAsync(ct);
        var rulesByArea = routingRules.ToDictionary(r => r.ProductArea, r => r.TargetTeam);

        var outcomesByArea = escalationOutcomes
            .Where(o => !string.IsNullOrEmpty(o.EscalationTraceId) && draftLookup.ContainsKey(o.EscalationTraceId!))
            .GroupBy(o => draftLookup[o.EscalationTraceId!].SuspectedComponent)
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .ToList();

        var productAreaMetrics = outcomesByArea.Select(g =>
        {
            var accepted = g.Count(o => o.Acceptance == true);
            var reroutedInArea = g.Count(o => o.ResolutionType == ResolutionType.Rerouted);
            var total = g.Count();
            var currentTeam = rulesByArea.GetValueOrDefault(g.Key!)
                              ?? draftLookup.Values.FirstOrDefault(d => d.SuspectedComponent == g.Key)?.TargetTeam
                              ?? "Unknown";

            return new ProductAreaRoutingMetrics
            {
                ProductArea = g.Key!,
                CurrentTargetTeam = currentTeam,
                TotalEscalations = total,
                AcceptedCount = accepted,
                ReroutedCount = reroutedInArea,
                AcceptanceRate = total > 0 ? (float)accepted / total : 0f,
                RerouteRate = total > 0 ? (float)reroutedInArea / total : 0f,
            };
        }).ToList();

        _logger.LogInformation(
            "Routing analytics computed. TenantId={TenantId}, WindowDays={WindowDays}, TotalOutcomes={TotalOutcomes}, Escalations={Escalations}, Reroutes={Reroutes}",
            tenantId, days, totalOutcomes, totalEscalations, rerouted.Count);

        return new RoutingAnalyticsSummary
        {
            TenantId = tenantId,
            TotalOutcomes = totalOutcomes,
            TotalEscalations = totalEscalations,
            TotalReroutes = rerouted.Count,
            TotalResolvedWithoutEscalation = resolvedWithout.Count,
            OverallAcceptanceRate = totalEscalations > 0
                ? (float)escalated.Count(o => o.Acceptance == true) / totalEscalations
                : 0f,
            OverallRerouteRate = totalEscalations > 0
                ? (float)rerouted.Count / totalEscalations
                : 0f,
            SelfResolutionRate = totalOutcomes > 0
                ? (float)resolvedWithout.Count / totalOutcomes
                : 0f,
            TeamMetrics = teamMetrics,
            ProductAreaMetrics = productAreaMetrics,
            ComputedAt = now,
            WindowStart = windowStart,
            WindowEnd = now,
        };
    }
}
