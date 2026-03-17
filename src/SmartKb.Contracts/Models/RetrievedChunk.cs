namespace SmartKb.Contracts.Models;

/// <summary>
/// A single chunk returned from hybrid retrieval, with scores and citation metadata.
/// </summary>
public sealed record RetrievedChunk
{
    public required string ChunkId { get; init; }
    public required string EvidenceId { get; init; }
    public required string ChunkText { get; init; }
    public string? ChunkContext { get; init; }
    public required string Title { get; init; }
    public required string SourceUrl { get; init; }
    public required string SourceSystem { get; init; }
    public required string SourceType { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public string? ProductArea { get; init; }
    public required string AccessLabel { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Visibility level for defense-in-depth ACL enforcement in orchestration (P0-014).</summary>
    public required string Visibility { get; init; }

    /// <summary>Allowed groups for Restricted visibility. Used by orchestration-layer ACL guard (P0-014).</summary>
    public IReadOnlyList<string> AllowedGroups { get; init; } = [];

    /// <summary>Combined RRF-fused score (primary ranking score).</summary>
    public required double RrfScore { get; init; }

    /// <summary>Semantic reranker score, if applied.</summary>
    public double? SemanticScore { get; init; }

    /// <summary>
    /// Source index this result came from: "Evidence" or "Pattern" (P1-004).
    /// Defaults to "Evidence" for backward compatibility.
    /// </summary>
    public string ResultSource { get; init; } = "Evidence";

    /// <summary>
    /// Trust level for pattern results (P1-004). Null for evidence chunks.
    /// </summary>
    public string? TrustLevel { get; init; }

    /// <summary>
    /// Boosted score after applying trust/recency/authority boosts (P1-004).
    /// Equals RrfScore when no boosts are applied.
    /// </summary>
    public double BoostedScore { get; init; }
}
