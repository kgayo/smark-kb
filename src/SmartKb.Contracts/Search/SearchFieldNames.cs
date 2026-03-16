namespace SmartKb.Contracts.Search;

/// <summary>
/// Azure AI Search field names for the Evidence index.
/// Centralizes field name constants to ensure consistency between indexing and querying.
/// </summary>
public static class SearchFieldNames
{
    // Key
    public const string ChunkId = "chunk_id";

    // Searchable text fields
    public const string ChunkText = "chunk_text";
    public const string ChunkContext = "chunk_context";
    public const string Title = "title";

    // Vector field
    public const string EmbeddingVector = "embedding_vector";

    // Filterable metadata
    public const string TenantId = "tenant_id";
    public const string EvidenceId = "evidence_id";
    public const string SourceSystem = "source_system";
    public const string SourceType = "source_type";
    public const string Status = "status";
    public const string UpdatedAt = "updated_at";
    public const string ProductArea = "product_area";
    public const string Tags = "tags";

    // ACL security trimming fields
    public const string Visibility = "visibility";
    public const string AllowedGroups = "allowed_groups";
    public const string AccessLabel = "access_label";

    // Source linkage for citations
    public const string SourceUrl = "source_url";

    /// <summary>Vector search profile name for the Evidence index.</summary>
    public const string VectorProfileName = "evidence-vector-profile";

    /// <summary>Semantic configuration name for reranking.</summary>
    public const string SemanticConfigName = "evidence-semantic-config";

    /// <summary>Embedding dimensions (text-embedding-3-large at 1536).</summary>
    public const int EmbeddingDimensions = 1536;
}
