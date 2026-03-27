namespace SmartKb.Data.Entities;

/// <summary>
/// Caches query embeddings by content hash for cost optimization (P2-003).
/// Avoids redundant OpenAI Embeddings API calls for identical query text.
/// </summary>
public sealed class EmbeddingCacheEntity
{
    public Guid Id { get; set; }

    /// <summary>SHA-256 hash of the input text. Used as lookup key.</summary>
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>The original input text (for collision detection).</summary>
    public string InputText { get; set; } = string.Empty;

    /// <summary>Serialized embedding vector as JSON float array.</summary>
    public string EmbeddingJson { get; set; } = string.Empty;

    /// <summary>Embedding model used to generate this embedding.</summary>
    public string ModelId { get; set; } = string.Empty;

    /// <summary>Number of dimensions in the embedding vector.</summary>
    public int Dimensions { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Last time this cache entry was accessed (for LRU eviction).</summary>
    public DateTimeOffset LastAccessedAt { get; set; }

    /// <summary>Cache expiry time based on TTL setting.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Unix epoch seconds of <see cref="ExpiresAt"/>. Enables server-side filtering in SQLite (which cannot compare DateTimeOffset).</summary>
    public long ExpiresAtEpoch { get; set; }
}
