using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

/// <summary>
/// Service for managing per-tenant retrieval tuning overrides (P1-007).
/// </summary>
public interface ITenantRetrievalSettingsService
{
    /// <summary>
    /// Gets effective retrieval settings for a tenant (overrides merged with global defaults).
    /// </summary>
    Task<RetrievalSettingsResponse> GetSettingsAsync(string tenantId, CancellationToken ct = default);

    /// <summary>
    /// Updates per-tenant retrieval settings. Only non-null fields in the request are overridden.
    /// </summary>
    Task<RetrievalSettingsResponse> UpdateSettingsAsync(
        string tenantId, UpdateRetrievalSettingsRequest request, CancellationToken ct = default);

    /// <summary>
    /// Resets per-tenant overrides to global defaults (deletes the override row).
    /// </summary>
    Task<bool> ResetSettingsAsync(string tenantId, CancellationToken ct = default);
}
