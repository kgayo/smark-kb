namespace SmartKb.Contracts.Configuration;

/// <summary>
/// Chunking strategy configuration. Decision D-002 resolved:
/// 512 tokens per chunk with 64 token overlap (~12.5%).
/// Respects markdown structure boundaries (headers, code blocks, lists)
/// with token-based fallback for unstructured content.
/// </summary>
public sealed class ChunkingSettings
{
    public const string SectionName = "Chunking";

    /// <summary>Maximum tokens per chunk.</summary>
    public int MaxTokensPerChunk { get; set; } = 512;

    /// <summary>Token overlap between consecutive chunks to preserve context at boundaries.</summary>
    public int OverlapTokens { get; set; } = 64;

    /// <summary>Whether to respect markdown structure (headers, code blocks, lists) as chunk boundaries.</summary>
    public bool UseStructuralBoundaries { get; set; } = true;
}
