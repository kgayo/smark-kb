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
}
