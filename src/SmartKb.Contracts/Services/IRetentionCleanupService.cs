using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

/// <summary>
/// Manages retention policy CRUD and executes cleanup for expired data (P2-001).
/// </summary>
public interface IRetentionCleanupService
{
    Task<RetentionPolicyResponse> GetPoliciesAsync(string tenantId, CancellationToken ct = default);
    Task<RetentionPolicyEntry> UpsertPolicyAsync(string tenantId, RetentionPolicyUpdateRequest request, string actorId, CancellationToken ct = default);
    Task<bool> DeletePolicyAsync(string tenantId, string entityType, string actorId, CancellationToken ct = default);
    Task<IReadOnlyList<RetentionCleanupResult>> ExecuteCleanupAsync(string tenantId, CancellationToken ct = default);
}
