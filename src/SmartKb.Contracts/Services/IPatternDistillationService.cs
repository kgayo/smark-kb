using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

/// <summary>
/// Discovers solved-ticket candidates and distills them into draft case patterns.
/// P1-005: Solved-ticket pattern distillation pipeline.
/// </summary>
public interface IPatternDistillationService
{
    /// <summary>
    /// Queries for sessions that meet D-008 solved-ticket criteria:
    /// Status in (Closed, Resolved) AND ResolvedWithoutEscalation AND positive_feedback >= min.
    /// </summary>
    Task<DistillationCandidateListResponse> FindCandidatesAsync(
        string tenantId, CancellationToken ct = default);

    /// <summary>
    /// Distills patterns from eligible candidates. Creates CasePatternEntity rows
    /// at Draft trust level, generates embeddings, and indexes into Pattern Store.
    /// </summary>
    Task<DistillationResult> DistillAsync(
        string tenantId, string actorId, string correlationId,
        CancellationToken ct = default);
}
