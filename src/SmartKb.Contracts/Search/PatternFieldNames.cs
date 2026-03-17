namespace SmartKb.Contracts.Search;

/// <summary>
/// Azure AI Search field names for the Case-Pattern index.
/// Centralizes field name constants to ensure consistency between indexing and querying.
/// </summary>
public static class PatternFieldNames
{
    // Key
    public const string PatternId = "pattern_id";

    // Searchable text fields
    public const string Title = "title";
    public const string ProblemStatement = "problem_statement";
    public const string Symptoms = "symptoms";
    public const string ResolutionSteps = "resolution_steps";

    // Vector field
    public const string EmbeddingVector = "pattern_embedding_vector";

    // Filterable metadata
    public const string TenantId = "tenant_id";
    public const string TrustLevel = "trust_level";
    public const string ProductArea = "product_area";
    public const string UpdatedAt = "updated_at";
    public const string Confidence = "confidence";
    public const string Tags = "tags";
    public const string Version = "version";

    // ACL security trimming fields
    public const string Visibility = "visibility";
    public const string AllowedGroups = "allowed_groups";
    public const string AccessLabel = "access_label";

    // Source linkage
    public const string SourceUrl = "source_url";

    /// <summary>Vector search profile name for the Pattern index.</summary>
    public const string VectorProfileName = "pattern-vector-profile";

    /// <summary>Semantic configuration name for pattern reranking.</summary>
    public const string SemanticConfigName = "pattern-semantic-config";

    /// <summary>Embedding dimensions (same text-embedding-3-large at 1536 as Evidence index).</summary>
    public const int EmbeddingDimensions = 1536;
}
