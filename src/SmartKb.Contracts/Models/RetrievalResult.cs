namespace SmartKb.Contracts.Models;

/// <summary>
/// Result of hybrid retrieval: ranked chunks with ACL-filtered count, no-evidence indicator, and trace ID.
/// </summary>
public sealed record RetrievalResult
{
    /// <summary>Ranked chunks after ACL filtering (highest score first).</summary>
    public required IReadOnlyList<RetrievedChunk> Chunks { get; init; }

    /// <summary>Number of results removed by ACL security trimming.</summary>
    public required int AclFilteredOutCount { get; init; }

    /// <summary>
    /// True if sufficient evidence was found (>= MinResults above score threshold).
    /// When false, the orchestration layer should trigger next-step/escalation path.
    /// </summary>
    public required bool HasEvidence { get; init; }

    /// <summary>Correlation/trace ID for audit trail.</summary>
    public required string TraceId { get; init; }

    /// <summary>
    /// Number of results that came from the Pattern index (P1-004).
    /// Zero when pattern fusion is disabled or no patterns matched.
    /// </summary>
    public int PatternCount { get; init; }
}
