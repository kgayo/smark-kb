using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

/// <summary>
/// Manages gold dataset evaluation cases for in-app CRUD and promotion from feedback (P3-022).
/// </summary>
public interface IGoldCaseService
{
    Task<GoldCaseDetail> CreateAsync(string tenantId, CreateGoldCaseRequest request, string actorId, CancellationToken ct = default);
    Task<GoldCaseDetail?> GetAsync(string tenantId, Guid id, CancellationToken ct = default);
    Task<GoldCaseListResponse> ListAsync(string tenantId, string? tag = null, int page = 1, int pageSize = 20, CancellationToken ct = default);
    Task<GoldCaseDetail?> UpdateAsync(string tenantId, Guid id, UpdateGoldCaseRequest request, string actorId, CancellationToken ct = default);
    Task<bool> DeleteAsync(string tenantId, Guid id, string actorId, CancellationToken ct = default);
    Task<GoldCaseDetail> PromoteFromFeedbackAsync(string tenantId, PromoteFromFeedbackRequest request, string actorId, CancellationToken ct = default);
    Task<string> ExportAsJsonlAsync(string tenantId, CancellationToken ct = default);
}
