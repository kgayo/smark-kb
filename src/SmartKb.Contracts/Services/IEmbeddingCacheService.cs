namespace SmartKb.Contracts.Services;

/// <summary>
/// Caches embedding vectors by content hash to reduce OpenAI API calls (P2-003).
/// </summary>
public interface IEmbeddingCacheService
{
    Task<(float[]? Embedding, bool CacheHit)> GetOrGenerateAsync(
        string text,
        CancellationToken ct = default);

    Task<int> EvictExpiredAsync(CancellationToken ct = default);
}
