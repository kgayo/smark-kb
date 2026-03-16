namespace SmartKb.Data.Entities;

/// <summary>
/// Persisted evidence chunk with lineage linking to parent source record and tenant.
/// Stores normalized/enriched chunk metadata and text; embedding vector populated
/// during indexing (P0-011).
/// </summary>
public sealed class EvidenceChunkEntity
{
    /// <summary>Format: {EvidenceId}_chunk_{index}.</summary>
    public string ChunkId { get; set; } = string.Empty;

    public string EvidenceId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public Guid ConnectorId { get; set; }
    public int ChunkIndex { get; set; }
    public string ChunkText { get; set; } = string.Empty;
    public string? ChunkContext { get; set; }

    // Denormalized metadata for querying without joins.
    public string SourceSystem { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
    public string? ProductArea { get; set; }
    public string? Tags { get; set; } // JSON array
    public string Visibility { get; set; } = string.Empty;
    public string? AllowedGroups { get; set; } // JSON array
    public string AccessLabel { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public string? ErrorTokens { get; set; } // JSON array
    public int EnrichmentVersion { get; set; } = 1;
    public string ContentHash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReprocessedAt { get; set; }

    // Navigation.
    public ConnectorEntity Connector { get; set; } = null!;
}
