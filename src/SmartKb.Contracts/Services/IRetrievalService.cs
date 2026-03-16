using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

/// <summary>
/// Hybrid retrieval service: vector + BM25 search with RRF score fusion,
/// optional semantic reranking, and ACL security trimming.
/// Phase 1 retrieves from Evidence Index only; Pattern Index fusion deferred to P1-004.
/// </summary>
public interface IRetrievalService
{
    /// <summary>
    /// Retrieves ranked evidence chunks for a query with tenant isolation and ACL filtering.
    /// </summary>
    /// <param name="tenantId">Tenant ID for hard isolation filter.</param>
    /// <param name="query">Natural language query text.</param>
    /// <param name="queryEmbedding">Pre-computed embedding vector for the query (1536 dims).</param>
    /// <param name="userGroups">Groups the calling user belongs to, for ACL filtering. Null = no restricted access.</param>
    /// <param name="correlationId">Optional correlation ID for tracing. Generated if null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Retrieval result with ranked chunks, ACL-filtered count, and no-evidence indicator.</returns>
    Task<RetrievalResult> RetrieveAsync(
        string tenantId,
        string query,
        float[] queryEmbedding,
        IReadOnlyList<string>? userGroups = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default);
}
