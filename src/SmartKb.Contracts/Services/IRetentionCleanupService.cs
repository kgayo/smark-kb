using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

/// <summary>
/// Manages retention policy CRUD, executes cleanup for expired data (P2-001),
/// and provides measurable execution history and compliance verification (P2-005).
/// </summary>
public interface IRetentionCleanupService
{
    Task<RetentionPolicyResponse> GetPoliciesAsync(string tenantId, CancellationToken ct = default);
    Task<RetentionPolicyEntry> UpsertPolicyAsync(string tenantId, RetentionPolicyUpdateRequest request, string actorId, CancellationToken ct = default);
    Task<bool> DeletePolicyAsync(string tenantId, string entityType, string actorId, CancellationToken ct = default);
    Task<IReadOnlyList<RetentionCleanupResult>> ExecuteCleanupAsync(string tenantId, CancellationToken ct = default);
    Task<RetentionExecutionHistoryResponse> GetExecutionHistoryAsync(string tenantId, string? entityType = null, int skip = 0, int take = 50, CancellationToken ct = default);
    Task<RetentionComplianceReport> GetComplianceReportAsync(string tenantId, CancellationToken ct = default);
}
