using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

/// <summary>
/// P2-004: Detects contradictions between case patterns — patterns with overlapping
/// problem domains but conflicting resolution steps. All detections require human review.
/// </summary>
public interface IContradictionDetectionService
{
    /// <summary>
    /// Scans active (non-deprecated) patterns for contradictions within a tenant.
    /// Creates PatternContradiction records for new conflicts found.
    /// </summary>
    Task<ContradictionDetectionResult> DetectContradictionsAsync(
        string tenantId, string actorId, string correlationId);

    /// <summary>Queries contradictions with optional status filter and pagination.</summary>
    Task<ContradictionListResponse> GetContradictionsAsync(
        string tenantId, string? status, int page, int pageSize);

    /// <summary>Resolves a contradiction (human review gate).</summary>
    Task<ContradictionSummary?> ResolveContradictionAsync(
        Guid contradictionId, string tenantId, string actorId,
        string correlationId, ResolveContradictionRequest request);
}
