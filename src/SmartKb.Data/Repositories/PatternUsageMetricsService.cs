using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data.Entities;

namespace SmartKb.Data.Repositories;

/// <summary>
/// Computes pattern usage metrics on-demand from answer trace citation data.
/// No materialized table — metrics are derived directly from AnswerTraceEntity.
/// </summary>
public sealed class PatternUsageMetricsService : IPatternUsageMetricsService
{
    private readonly SmartKbDbContext _db;
    private readonly TimeProvider _time;
    private readonly ILogger<PatternUsageMetricsService> _logger;

    public PatternUsageMetricsService(
        SmartKbDbContext db,
        ILogger<PatternUsageMetricsService> logger,
        TimeProvider? time = null)
    {
        _db = db;
        _time = time ?? TimeProvider.System;
        _logger = logger;
    }

    public async Task<PatternUsageMetrics> GetUsageAsync(
        string tenantId, string patternId, CancellationToken ct = default)
    {
        var now = _time.GetUtcNow();
        var cutoff90 = now.AddDays(-90);
        var cutoff90Epoch = cutoff90.ToUnixTimeSeconds();

        // Filter by tenant + 90-day window server-side via epoch column (SQLite cannot compare DateTimeOffset).
        // CitedChunkIds JSON column parsing still requires in-memory iteration.
        var traces = await _db.Set<AnswerTraceEntity>()
            .AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.CreatedAtEpoch >= cutoff90Epoch)
            .Select(t => new { t.CitedChunkIds, t.UserId, t.Confidence, t.CreatedAt })
            .ToListAsync(ct);

        var cutoff7 = now.AddDays(-7);
        var cutoff30 = now.AddDays(-30);
        var dailyStart = DateOnly.FromDateTime(now.AddDays(-29).UtcDateTime);

        int total = 0, last7 = 0, last30 = 0, last90 = 0;
        var uniqueUsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        float confidenceSum = 0;
        DateTimeOffset? firstCited = null, lastCited = null;
        var dailyCounts = new Dictionary<DateOnly, int>();

        foreach (var trace in traces)
        {
            var patternIds = ExtractPatternIds(trace.CitedChunkIds);
            if (!patternIds.Contains(patternId)) continue;

            total++;
            last90++;
            if (trace.CreatedAt >= cutoff30) last30++;
            if (trace.CreatedAt >= cutoff7) last7++;

            uniqueUsers.Add(trace.UserId);
            confidenceSum += trace.Confidence;

            if (firstCited is null || trace.CreatedAt < firstCited) firstCited = trace.CreatedAt;
            if (lastCited is null || trace.CreatedAt > lastCited) lastCited = trace.CreatedAt;

            var day = DateOnly.FromDateTime(trace.CreatedAt.UtcDateTime);
            if (day >= dailyStart)
            {
                dailyCounts[day] = dailyCounts.GetValueOrDefault(day) + 1;
            }
        }

        // Build 30-day daily breakdown (fill in zeros)
        var breakdown = new List<PatternUsageDayBucket>();
        for (int i = 0; i < 30; i++)
        {
            var d = dailyStart.AddDays(i);
            breakdown.Add(new PatternUsageDayBucket
            {
                Date = d,
                Citations = dailyCounts.GetValueOrDefault(d),
            });
        }

        _logger.LogDebug("Pattern usage computed for {PatternId}: {Total} total citations", patternId, total);

        return new PatternUsageMetrics
        {
            PatternId = patternId,
            TotalCitations = total,
            CitationsLast7Days = last7,
            CitationsLast30Days = last30,
            CitationsLast90Days = last90,
            UniqueUsers = uniqueUsers.Count,
            AverageConfidence = total > 0 ? confidenceSum / total : 0,
            LastCitedAt = lastCited,
            FirstCitedAt = firstCited,
            DailyBreakdown = breakdown,
        };
    }

    internal HashSet<string> ExtractPatternIds(string citedChunkIdsJson)
        => PatternIdHelper.ExtractPatternIds(citedChunkIdsJson, _logger);
}
