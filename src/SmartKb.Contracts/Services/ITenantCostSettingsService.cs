using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

/// <summary>
/// Manages per-tenant cost optimization settings (P2-003).
/// </summary>
public interface ITenantCostSettingsService
{
    Task<CostSettingsResponse> GetSettingsAsync(string tenantId, CancellationToken ct = default);
    Task<CostSettingsResponse> UpdateSettingsAsync(string tenantId, UpdateCostSettingsRequest request, CancellationToken ct = default);
    Task<bool> ResetSettingsAsync(string tenantId, CancellationToken ct = default);
}
