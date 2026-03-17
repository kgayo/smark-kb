namespace SmartKb.Contracts.Configuration;

/// <summary>
/// Configuration for the hybrid retrieval service.
/// Design decisions D-005 (top-k/RRF weights) and D-012 (no-evidence threshold) resolved here.
/// </summary>
public sealed class RetrievalSettings
{
    public const string SectionName = "Retrieval";

    /// <summary>
    /// Maximum number of results to return from hybrid search (D-005: default 20).
    /// </summary>
    public int TopK { get; set; } = 20;

    /// <summary>
    /// RRF constant k used in score fusion: score = 1 / (k + rank). Standard default 60.
    /// </summary>
    public int RrfK { get; set; } = 60;

    /// <summary>
    /// Weight applied to BM25 RRF scores before fusion (D-005: default 1.0, equal weighting).
    /// </summary>
    public float Bm25Weight { get; set; } = 1.0f;

    /// <summary>
    /// Weight applied to vector RRF scores before fusion (D-005: default 1.0, equal weighting).
    /// </summary>
    public float VectorWeight { get; set; } = 1.0f;

    /// <summary>
    /// Enable semantic reranking on merged top-k results.
    /// </summary>
    public bool EnableSemanticReranking { get; set; } = true;

    /// <summary>
    /// Minimum score threshold for a result to count toward evidence (D-012: default 0.3).
    /// </summary>
    public float NoEvidenceScoreThreshold { get; set; } = 0.3f;

    /// <summary>
    /// Minimum number of results above score threshold to consider evidence sufficient (D-012: default 3).
    /// </summary>
    public int NoEvidenceMinResults { get; set; } = 3;

    // --- Pattern fusion settings (P1-004) ---

    /// <summary>
    /// Enable cross-index retrieval fusion (Evidence + Pattern indexes).
    /// When false, retrieval uses Evidence index only (Phase 1 behavior).
    /// </summary>
    public bool EnablePatternFusion { get; set; } = true;

    /// <summary>
    /// Maximum number of pattern results to retrieve from the Pattern index.
    /// </summary>
    public int PatternTopK { get; set; } = 5;

    /// <summary>
    /// Trust level boost multiplier for approved patterns.
    /// </summary>
    public float TrustBoostApproved { get; set; } = 1.5f;

    /// <summary>
    /// Trust level boost multiplier for reviewed patterns.
    /// </summary>
    public float TrustBoostReviewed { get; set; } = 1.2f;

    /// <summary>
    /// Trust level boost multiplier for draft patterns.
    /// </summary>
    public float TrustBoostDraft { get; set; } = 0.8f;

    /// <summary>
    /// Trust level boost multiplier for deprecated patterns.
    /// </summary>
    public float TrustBoostDeprecated { get; set; } = 0.3f;

    /// <summary>
    /// Recency boost: multiplier for results updated within the last 30 days.
    /// </summary>
    public float RecencyBoostRecent { get; set; } = 1.2f;

    /// <summary>
    /// Recency boost: multiplier for results older than 90 days.
    /// </summary>
    public float RecencyBoostOld { get; set; } = 0.8f;

    /// <summary>
    /// Base authority boost for pattern results (curated knowledge premium).
    /// </summary>
    public float PatternAuthorityBoost { get; set; } = 1.3f;

    /// <summary>
    /// Maximum number of chunks from the same source (evidence_id or pattern_id) in final results.
    /// Enforces diversity constraint.
    /// </summary>
    public int DiversityMaxPerSource { get; set; } = 3;
}
