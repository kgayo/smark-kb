using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

public interface IOutcomeService
{
    Task<OutcomeResponse> RecordOutcomeAsync(
        string tenantId, string userId, string correlationId,
        Guid sessionId,
        RecordOutcomeRequest request, CancellationToken ct = default);

    Task<OutcomeListResponse?> GetOutcomesAsync(
        string tenantId, string userId,
        Guid sessionId, CancellationToken ct = default);
}
