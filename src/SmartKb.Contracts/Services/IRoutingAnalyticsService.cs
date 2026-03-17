using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

public interface IRoutingAnalyticsService
{
    Task<RoutingAnalyticsSummary> GetAnalyticsAsync(
        string tenantId,
        int? windowDays = null,
        CancellationToken ct = default);
}
