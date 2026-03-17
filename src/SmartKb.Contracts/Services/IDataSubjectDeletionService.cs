using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

/// <summary>
/// Handles right-to-delete requests: purge all data for a given data subject
/// across sessions, messages, feedback, traces, escalation drafts, and outcome events (P2-001).
/// </summary>
public interface IDataSubjectDeletionService
{
    Task<DataSubjectDeletionResponse> RequestDeletionAsync(string tenantId, string subjectId, string requestedBy, CancellationToken ct = default);
    Task<DataSubjectDeletionResponse?> GetDeletionRequestAsync(string tenantId, Guid requestId, CancellationToken ct = default);
    Task<DataSubjectDeletionListResponse> ListDeletionRequestsAsync(string tenantId, CancellationToken ct = default);
}
