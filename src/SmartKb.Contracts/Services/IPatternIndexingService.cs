using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

/// <summary>
/// Manages the Azure AI Search Pattern index lifecycle and document indexing.
/// Analogous to IIndexingService for the Evidence index.
/// </summary>
public interface IPatternIndexingService
{
    /// <summary>
    /// Ensures the Pattern index exists with the correct schema, vector profile,
    /// and semantic configuration. Idempotent — creates or updates.
    /// </summary>
    Task EnsureIndexAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Indexes a batch of case patterns into the Pattern search index.
    /// Patterns must have EmbeddingVector populated (non-null).
    /// </summary>
    Task<IndexingResult> IndexPatternsAsync(
        IReadOnlyList<CasePattern> patterns,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes patterns from the search index by their pattern IDs.
    /// </summary>
    Task<int> DeletePatternsAsync(
        IReadOnlyList<string> patternIds,
        CancellationToken cancellationToken = default);
}
