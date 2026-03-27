using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Services;
using SmartKb.Data.Entities;

namespace SmartKb.Data.Repositories;

public sealed class RateLimitAlertService : IRateLimitAlertService
{
    private readonly SmartKbDbContext _db;
    private readonly SloSettings _sloSettings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RateLimitAlertService> _logger;

    public RateLimitAlertService(
        SmartKbDbContext db,
        IOptions<SloSettings> sloSettings,
        TimeProvider timeProvider,
        ILogger<RateLimitAlertService> logger)
    {
        _db = db;
        _sloSettings = sloSettings.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task RecordRateLimitEventAsync(
        string tenantId, Guid connectorId, string connectorType, CancellationToken ct = default)
    {
        var occurredAt = _timeProvider.GetUtcNow();
        _db.RateLimitEvents.Add(new RateLimitEventEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ConnectorId = connectorId,
            ConnectorType = connectorType,
            OccurredAt = occurredAt,
            OccurredAtEpoch = occurredAt.ToUnixTimeSeconds(),
        });
        await _db.SaveChangesAsync(ct);

        _logger.LogWarning(
            "Rate-limit event recorded for connector {ConnectorId} (type={ConnectorType}, tenant={TenantId})",
            connectorId, connectorType, tenantId);
    }

    public async Task<RateLimitAlertSummary> GetRateLimitAlertsAsync(
        string tenantId, CancellationToken ct = default)
    {
        var windowStartEpoch = _timeProvider.GetUtcNow()
            .AddMinutes(-_sloSettings.RateLimitAlertWindowMinutes)
            .ToUnixTimeSeconds();

        // Filter server-side using the long epoch column (SQLite-compatible).
        var recentEvents = await _db.RateLimitEvents
            .Where(e => e.TenantId == tenantId && e.OccurredAtEpoch >= windowStartEpoch)
            .ToListAsync(ct);

        var grouped = recentEvents
            .GroupBy(e => new { e.ConnectorId, e.ConnectorType })
            .Select(g => new
            {
                g.Key.ConnectorId,
                g.Key.ConnectorType,
                HitCount = g.Count(),
                MostRecentHit = g.Max(e => e.OccurredAt),
            })
            .Where(g => g.HitCount >= _sloSettings.RateLimitAlertThreshold)
            .ToList();

        if (grouped.Count == 0)
        {
            return new RateLimitAlertSummary(0, Array.Empty<ConnectorRateLimitAlert>());
        }

        // Resolve connector names.
        var connectorIds = grouped.Select(e => e.ConnectorId).ToList();
        var connectors = await _db.Connectors
            .Where(c => c.TenantId == tenantId && connectorIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name })
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        var alerts = grouped.Select(e => new ConnectorRateLimitAlert(
            e.ConnectorId,
            connectors.GetValueOrDefault(e.ConnectorId, "Unknown"),
            e.ConnectorType,
            e.HitCount,
            e.MostRecentHit,
            _sloSettings.RateLimitAlertThreshold,
            _sloSettings.RateLimitAlertWindowMinutes
        )).ToList();

        return new RateLimitAlertSummary(alerts.Count, alerts);
    }
}
