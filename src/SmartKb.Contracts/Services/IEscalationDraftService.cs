using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

public interface IEscalationDraftService
{
    Task<EscalationDraftResponse> CreateDraftAsync(
        string tenantId, string userId, string correlationId,
        CreateEscalationDraftRequest request, CancellationToken ct = default);

    Task<EscalationDraftResponse?> GetDraftAsync(
        string tenantId, string userId, Guid draftId, CancellationToken ct = default);

    Task<EscalationDraftListResponse?> ListDraftsAsync(
        string tenantId, string userId, Guid sessionId, CancellationToken ct = default);

    Task<(EscalationDraftResponse? Response, bool NotFound)> UpdateDraftAsync(
        string tenantId, string userId, Guid draftId,
        UpdateEscalationDraftRequest request, CancellationToken ct = default);

    Task<EscalationDraftExportResponse?> ExportDraftAsMarkdownAsync(
        string tenantId, string userId, Guid draftId, CancellationToken ct = default);

    Task<bool> DeleteDraftAsync(
        string tenantId, string userId, Guid draftId, CancellationToken ct = default);

    Task<ExternalEscalationResult?> ApproveAndCreateExternalAsync(
        string tenantId, string userId, string correlationId,
        Guid draftId, ApproveEscalationDraftRequest request, CancellationToken ct = default);
}
