namespace SmartKb.Contracts.Models;

/// <summary>
/// Response DTO for per-tenant retrieval settings (P1-007).
/// Shows effective values (tenant override merged with global defaults).
/// </summary>
public sealed record RetrievalSettingsResponse
{
    public required string TenantId { get; init; }
    public required int TopK { get; init; }
    public required bool EnableSemanticReranking { get; init; }
    public required bool EnablePatternFusion { get; init; }
    public required int PatternTopK { get; init; }
    public required float TrustBoostApproved { get; init; }
    public required float TrustBoostReviewed { get; init; }
    public required float TrustBoostDraft { get; init; }
    public required float RecencyBoostRecent { get; init; }
    public required float RecencyBoostOld { get; init; }
    public required float PatternAuthorityBoost { get; init; }
    public required int DiversityMaxPerSource { get; init; }
    public required float NoEvidenceScoreThreshold { get; init; }
    public required int NoEvidenceMinResults { get; init; }
    public required bool HasOverrides { get; init; }
}

/// <summary>
/// Request DTO for updating per-tenant retrieval settings (P1-007).
/// All fields are optional — only non-null fields are overridden.
/// Set a field to its default value to clear the override.
/// </summary>
public sealed record UpdateRetrievalSettingsRequest
{
    public int? TopK { get; init; }
    public bool? EnableSemanticReranking { get; init; }
    public bool? EnablePatternFusion { get; init; }
    public int? PatternTopK { get; init; }
    public float? TrustBoostApproved { get; init; }
    public float? TrustBoostReviewed { get; init; }
    public float? TrustBoostDraft { get; init; }
    public float? RecencyBoostRecent { get; init; }
    public float? RecencyBoostOld { get; init; }
    public float? PatternAuthorityBoost { get; init; }
    public int? DiversityMaxPerSource { get; init; }
    public float? NoEvidenceScoreThreshold { get; init; }
    public int? NoEvidenceMinResults { get; init; }
}
