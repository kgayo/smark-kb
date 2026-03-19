using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

/// <summary>
/// Computes pattern usage/reuse metrics from answer trace citation data.
/// </summary>
public interface IPatternUsageMetricsService
{
    /// <summary>
    /// Computes usage metrics for a specific pattern by scanning answer traces.
    /// </summary>
    Task<PatternUsageMetrics> GetUsageAsync(
        string tenantId,
        string patternId,
        CancellationToken ct = default);
}
