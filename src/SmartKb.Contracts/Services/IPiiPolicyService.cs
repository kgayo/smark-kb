using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

/// <summary>
/// Manages per-tenant PII policy configuration (P2-001).
/// </summary>
public interface IPiiPolicyService
{
    Task<PiiPolicyResponse?> GetPolicyAsync(string tenantId, CancellationToken ct = default);
    Task<PiiPolicyResponse> UpsertPolicyAsync(string tenantId, PiiPolicyUpdateRequest request, string actorId, CancellationToken ct = default);
    Task<bool> DeletePolicyAsync(string tenantId, string actorId, CancellationToken ct = default);
}
