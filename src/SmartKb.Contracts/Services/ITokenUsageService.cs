using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

/// <summary>
/// Records and queries token usage for cost tracking and budget enforcement (P2-003).
/// </summary>
public interface ITokenUsageService
{
    Task RecordUsageAsync(
        string tenantId,
        string userId,
        string correlationId,
        TokenUsageRecord usage,
        CancellationToken ct = default);

    Task<TokenUsageSummary> GetSummaryAsync(
        string tenantId,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        CancellationToken ct = default);

    Task<IReadOnlyList<DailyUsageBreakdown>> GetDailyBreakdownAsync(
        string tenantId,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        CancellationToken ct = default);

    Task<BudgetCheckResult> CheckBudgetAsync(
        string tenantId,
        CancellationToken ct = default);
}
