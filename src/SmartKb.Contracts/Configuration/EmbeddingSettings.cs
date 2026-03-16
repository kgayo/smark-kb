namespace SmartKb.Contracts.Configuration;

/// <summary>
/// Embedding model configuration. Decision D-001 resolved:
/// text-embedding-3-large at 1536 dimensions (native dimension reduction from 3072).
/// Superior retrieval quality for support evidence at manageable index size.
/// </summary>
public sealed class EmbeddingSettings
{
    public const string SectionName = "Embedding";

    public string ModelId { get; set; } = "text-embedding-3-large";
    public int Dimensions { get; set; } = 1536;
    public int MaxInputTokens { get; set; } = 8191;
}
