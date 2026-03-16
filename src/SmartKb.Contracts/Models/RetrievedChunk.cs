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

    /// <summary>Combined RRF-fused score (primary ranking score).</summary>
    public required double RrfScore { get; init; }

    /// <summary>Semantic reranker score, if applied.</summary>
    public double? SemanticScore { get; init; }
}
