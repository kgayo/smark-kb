namespace SmartKb.Data.Entities;

/// <summary>
/// Per-tenant retrieval tuning overrides (P1-007).
/// Null fields fall back to global RetrievalSettings defaults.
/// One row per tenant.
/// </summary>
public sealed class TenantRetrievalSettingsEntity
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;

    // Basic retrieval overrides
    public int? TopK { get; set; }
    public bool? EnableSemanticReranking { get; set; }

    // Pattern fusion overrides
    public bool? EnablePatternFusion { get; set; }
    public int? PatternTopK { get; set; }

    // Boost overrides
    public float? TrustBoostApproved { get; set; }
    public float? TrustBoostReviewed { get; set; }
    public float? TrustBoostDraft { get; set; }
    public float? RecencyBoostRecent { get; set; }
    public float? RecencyBoostOld { get; set; }
    public float? PatternAuthorityBoost { get; set; }

    // Diversity overrides
    public int? DiversityMaxPerSource { get; set; }

    // No-evidence threshold overrides
    public float? NoEvidenceScoreThreshold { get; set; }
    public int? NoEvidenceMinResults { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public TenantEntity Tenant { get; set; } = null!;
}
