using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

/// <summary>
/// P1-006: Pattern governance workflows — trust-level transitions (review, approve, deprecate),
/// governance queue queries, and pattern detail retrieval.
/// </summary>
public interface IPatternGovernanceService
{
    /// <summary>
    /// Queries the governance queue: patterns filtered by trust level, product area, etc.
    /// </summary>
    Task<PatternGovernanceQueueResponse> GetGovernanceQueueAsync(
        string tenantId,
        string? trustLevel = null,
        string? productArea = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default);

    /// <summary>Gets full detail for a single pattern by its string PatternId.</summary>
    Task<PatternDetail?> GetPatternDetailAsync(
        string tenantId, string patternId, CancellationToken ct = default);

    /// <summary>Transitions a pattern from Draft to Reviewed.</summary>
    Task<PatternGovernanceResult?> ReviewPatternAsync(
        string tenantId, string patternId, string actorId, string correlationId,
        ReviewPatternRequest request, CancellationToken ct = default);

    /// <summary>Transitions a pattern from Draft or Reviewed to Approved.</summary>
    Task<PatternGovernanceResult?> ApprovePatternAsync(
        string tenantId, string patternId, string actorId, string correlationId,
        ApprovePatternRequest request, CancellationToken ct = default);

    /// <summary>Transitions a pattern from any non-deprecated state to Deprecated.</summary>
    Task<PatternGovernanceResult?> DeprecatePatternAsync(
        string tenantId, string patternId, string actorId, string correlationId,
        DeprecatePatternRequest request, CancellationToken ct = default);
}
