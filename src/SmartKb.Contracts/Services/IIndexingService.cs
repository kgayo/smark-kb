using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

/// <summary>
/// Manages the Azure AI Search Evidence index lifecycle and document indexing.
/// </summary>
public interface IIndexingService
{
    /// <summary>
    /// Ensures the Evidence index exists with the correct schema, vector profile,
    /// and semantic configuration. Idempotent — creates or updates.
    /// </summary>
    Task EnsureIndexAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Indexes a batch of evidence chunks into the search index.
    /// Chunks must have EmbeddingVector populated (non-null).
    /// </summary>
    Task<IndexingResult> IndexChunksAsync(
        IReadOnlyList<EvidenceChunk> chunks,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes documents from the search index by their chunk IDs.
    /// </summary>
    Task<int> DeleteChunksAsync(
        IReadOnlyList<string> chunkIds,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of an indexing operation.
/// </summary>
public sealed record IndexingResult(
    int Succeeded,
    int Failed,
    IReadOnlyList<string> FailedChunkIds);
