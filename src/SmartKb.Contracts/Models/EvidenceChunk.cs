using SmartKb.Contracts.Enums;

namespace SmartKb.Contracts.Models;

/// <summary>
/// A single chunk of evidence derived from a CanonicalRecord, ready for indexing
/// into Azure AI Search. Contains the text, vector embedding, and all filterable
/// metadata including ACL fields for security trimming.
/// </summary>
public sealed record EvidenceChunk
{
    /// <summary>Unique identifier for this chunk (typically {EvidenceId}_chunk_{index}).</summary>
    public required string ChunkId { get; init; }

    /// <summary>Parent evidence record ID for lineage tracking.</summary>
    public required string EvidenceId { get; init; }

    public required string TenantId { get; init; }

    /// <summary>Zero-based index of this chunk within the parent record.</summary>
    public required int ChunkIndex { get; init; }

    /// <summary>The chunked text content for BM25 keyword search.</summary>
    public required string ChunkText { get; init; }

    /// <summary>Contextual header path (e.g. section hierarchy) for display.</summary>
    public string? ChunkContext { get; init; }

    /// <summary>Vector embedding (1536 dimensions from text-embedding-3-large).</summary>
    public float[]? EmbeddingVector { get; init; }

    // Filterable metadata (denormalized from parent CanonicalRecord for search filtering)
    public required ConnectorType SourceSystem { get; init; }
    public required SourceType SourceType { get; init; }
    public required EvidenceStatus Status { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public string? ProductArea { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];

    // ACL fields for security trimming
    public required AccessVisibility Visibility { get; init; }
    public required IReadOnlyList<string> AllowedGroups { get; init; }

    /// <summary>Human-readable access label for Evidence Drawer display.</summary>
    public required string AccessLabel { get; init; }

    // Source linkage for citation display
    public required string Title { get; init; }
    public required string SourceUrl { get; init; }

    /// <summary>Monotonic enrichment version for safe reprocessing.</summary>
    public int EnrichmentVersion { get; init; } = 1;

    /// <summary>Error tokens extracted during baseline enrichment.</summary>
    public IReadOnlyList<string> ErrorTokens { get; init; } = [];
}
