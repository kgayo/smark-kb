using System.Diagnostics;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Search;

namespace SmartKb.Contracts.Services;

/// <summary>
/// Azure AI Search implementation of IRetrievalService.
/// Performs hybrid retrieval: vector + BM25 search, RRF score fusion, optional semantic reranking,
/// and ACL security trimming. Emits retrieval telemetry for audit.
/// </summary>
public sealed class AzureSearchRetrievalService : IRetrievalService
{
    private readonly SearchIndexClient _indexClient;
    private readonly SearchServiceSettings _searchSettings;
    private readonly RetrievalSettings _retrievalSettings;
    private readonly ISearchTokenService? _searchTokenService;
    private readonly ILogger<AzureSearchRetrievalService> _logger;

    public AzureSearchRetrievalService(
        SearchIndexClient indexClient,
        SearchServiceSettings searchSettings,
        RetrievalSettings retrievalSettings,
        ILogger<AzureSearchRetrievalService> logger,
        ISearchTokenService? searchTokenService = null)
    {
        _indexClient = indexClient;
        _searchSettings = searchSettings;
        _retrievalSettings = retrievalSettings;
        _logger = logger;
        _searchTokenService = searchTokenService;
    }

    public async Task<RetrievalResult> RetrieveAsync(
        string tenantId,
        string query,
        float[] queryEmbedding,
        IReadOnlyList<string>? userGroups = null,
        RetrievalFilter? filters = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        var traceId = correlationId ?? Activity.Current?.Id ?? Guid.NewGuid().ToString("N");
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "Retrieval started. TraceId={TraceId} TenantId={TenantId} TopK={TopK} SemanticRerank={SemanticRerank} HasFilters={HasFilters}",
            traceId, tenantId, _retrievalSettings.TopK, _retrievalSettings.EnableSemanticReranking,
            filters is not null && !filters.IsEmpty);

        var searchClient = _indexClient.GetSearchClient(_searchSettings.EvidenceIndexName);

        // P3-028: Preprocess query — remove stop words, boost special tokens for BM25.
        var searchQuery = query;
        if (_searchTokenService is not null)
        {
            try
            {
                var preprocessed = await _searchTokenService.PreprocessQueryAsync(tenantId, query, cancellationToken);
                searchQuery = preprocessed.BoostedQuery;

                if (preprocessed.RemovedStopWords.Count > 0 || preprocessed.DetectedSpecialTokens.Count > 0)
                {
                    _logger.LogInformation(
                        "Query preprocessed. TraceId={TraceId} StopWordsRemoved={StopWordsRemoved} SpecialTokens={SpecialTokens}",
                        traceId, string.Join(", ", preprocessed.RemovedStopWords),
                        string.Join(", ", preprocessed.DetectedSpecialTokens));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Query preprocessing failed, using original query. TraceId={TraceId}", traceId);
            }
        }

        // Build tenant isolation filter (always applied server-side).
        var tenantFilter = $"{SearchFieldNames.TenantId} eq '{EscapeODataValue(tenantId)}'";

        // Combine with optional metadata filters (P1-007).
        var additionalFilter = ODataFilterBuilder.BuildEvidenceFilter(filters);
        var combinedFilter = ODataFilterBuilder.CombineFilters(tenantFilter, additionalFilter);

        // Build search options for hybrid query: text (BM25) + vector, with optional semantic reranking.
        var options = new SearchOptions
        {
            Filter = combinedFilter,
            Size = _retrievalSettings.TopK * 2, // Over-fetch to account for ACL filtering post-retrieval.
            Select =
            {
                SearchFieldNames.ChunkId,
                SearchFieldNames.EvidenceId,
                SearchFieldNames.ChunkText,
                SearchFieldNames.ChunkContext,
                SearchFieldNames.Title,
                SearchFieldNames.SourceUrl,
                SearchFieldNames.SourceSystem,
                SearchFieldNames.SourceType,
                SearchFieldNames.UpdatedAt,
                SearchFieldNames.ProductArea,
                SearchFieldNames.AccessLabel,
                SearchFieldNames.Tags,
                SearchFieldNames.Visibility,
                SearchFieldNames.AllowedGroups,
            },
            SearchFields =
            {
                SearchFieldNames.ChunkText,
                SearchFieldNames.ChunkContext,
                SearchFieldNames.Title,
            },
            VectorSearch = new VectorSearchOptions
            {
                Queries =
                {
                    new VectorizedQuery(queryEmbedding)
                    {
                        KNearestNeighborsCount = _retrievalSettings.TopK * 2,
                        Fields = { SearchFieldNames.EmbeddingVector },
                    },
                },
            },
        };

        // Enable semantic reranking if configured.
        if (_retrievalSettings.EnableSemanticReranking)
        {
            options.SemanticSearch = new SemanticSearchOptions
            {
                SemanticConfigurationName = SearchFieldNames.SemanticConfigName,
                QueryCaption = new QueryCaption(QueryCaptionType.Extractive),
            };
            options.QueryType = SearchQueryType.Semantic;
        }

        // Execute hybrid search.
        List<RawSearchResult> rawResults;
        try
        {
            rawResults = await ExecuteSearchAsync(searchClient, searchQuery, options, cancellationToken);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex,
                "Azure AI Search query failed. TraceId={TraceId} TenantId={TenantId} Status={Status}",
                traceId, tenantId, ex.Status);

            return new RetrievalResult
            {
                Chunks = [],
                AclFilteredOutCount = 0,
                HasEvidence = false,
                TraceId = traceId,
            };
        }

        // Apply ACL security trimming in-memory.
        var (aclFiltered, aclFilteredOutCount) = ApplyAclFilter(rawResults, userGroups);

        // Take top-k after ACL filtering.
        var topResults = aclFiltered
            .Take(_retrievalSettings.TopK)
            .ToList();

        // Map to RetrievedChunk DTOs.
        var chunks = topResults.Select(r => new RetrievedChunk
        {
            ChunkId = r.ChunkId,
            EvidenceId = r.EvidenceId,
            ChunkText = r.ChunkText,
            ChunkContext = r.ChunkContext,
            Title = r.Title,
            SourceUrl = r.SourceUrl,
            SourceSystem = r.SourceSystem,
            SourceType = r.SourceType,
            UpdatedAt = r.UpdatedAt,
            ProductArea = r.ProductArea,
            AccessLabel = r.AccessLabel,
            Tags = r.Tags,
            Visibility = r.Visibility,
            AllowedGroups = r.AllowedGroups,
            RrfScore = r.Score,
            SemanticScore = r.SemanticScore,
        }).ToList();

        // Evaluate no-evidence condition (D-012).
        var aboveThresholdCount = chunks.Count(c =>
            c.RrfScore >= _retrievalSettings.NoEvidenceScoreThreshold);
        var hasEvidence = aboveThresholdCount >= _retrievalSettings.NoEvidenceMinResults;

        stopwatch.Stop();

        // Emit retrieval telemetry.
        _logger.LogInformation(
            "Retrieval completed. TraceId={TraceId} TenantId={TenantId} " +
            "TotalRaw={TotalRaw} AclFilteredOut={AclFilteredOut} Returned={Returned} " +
            "HasEvidence={HasEvidence} AboveThreshold={AboveThreshold} " +
            "DurationMs={DurationMs} TopIds={TopIds} TopScores={TopScores}",
            traceId, tenantId,
            rawResults.Count, aclFilteredOutCount, chunks.Count,
            hasEvidence, aboveThresholdCount,
            stopwatch.ElapsedMilliseconds,
            string.Join(",", chunks.Take(5).Select(c => c.ChunkId)),
            string.Join(",", chunks.Take(5).Select(c => c.RrfScore.ToString("F4"))));

        return new RetrievalResult
        {
            Chunks = chunks,
            AclFilteredOutCount = aclFilteredOutCount,
            HasEvidence = hasEvidence,
            TraceId = traceId,
        };
    }

    private async Task<List<RawSearchResult>> ExecuteSearchAsync(
        SearchClient searchClient,
        string query,
        SearchOptions options,
        CancellationToken cancellationToken)
    {
        var results = new List<RawSearchResult>();
        var response = await searchClient.SearchAsync<SearchDocument>(query, options, cancellationToken);

        await foreach (var result in response.Value.GetResultsAsync())
        {
            var doc = result.Document;

            var raw = new RawSearchResult
            {
                ChunkId = GetStringValue(doc, SearchFieldNames.ChunkId),
                EvidenceId = GetStringValue(doc, SearchFieldNames.EvidenceId),
                ChunkText = GetStringValue(doc, SearchFieldNames.ChunkText),
                ChunkContext = GetStringOrNull(doc, SearchFieldNames.ChunkContext),
                Title = GetStringValue(doc, SearchFieldNames.Title),
                SourceUrl = GetStringValue(doc, SearchFieldNames.SourceUrl),
                SourceSystem = GetStringValue(doc, SearchFieldNames.SourceSystem),
                SourceType = GetStringValue(doc, SearchFieldNames.SourceType),
                UpdatedAt = doc.TryGetValue(SearchFieldNames.UpdatedAt, out var updatedAtObj) && updatedAtObj is DateTimeOffset dto
                    ? dto
                    : DateTimeOffset.MinValue,
                ProductArea = GetStringOrNull(doc, SearchFieldNames.ProductArea),
                AccessLabel = GetStringValue(doc, SearchFieldNames.AccessLabel),
                Tags = GetStringList(doc, SearchFieldNames.Tags),
                Visibility = GetStringValue(doc, SearchFieldNames.Visibility),
                AllowedGroups = GetStringList(doc, SearchFieldNames.AllowedGroups),
                // Azure AI Search hybrid query returns a combined score via result.Score.
                // When semantic reranking is enabled, SemanticSearch.RerankerScore has the reranker score.
                Score = result.Score ?? 0.0,
                SemanticScore = result.SemanticSearch?.RerankerScore,
            };

            results.Add(raw);
        }

        return results;
    }

    /// <summary>
    /// Applies ACL security trimming: restricted documents are only returned if user is in allowed_groups.
    /// Internal and public documents pass through for all authenticated users.
    /// CRITICAL: This ensures restricted content never reaches the model (jtbd-03, jtbd-10).
    /// </summary>
    internal static (List<RawSearchResult> Filtered, int FilteredOutCount) ApplyAclFilter(
        List<RawSearchResult> results,
        IReadOnlyList<string>? userGroups)
    {
        var filtered = new List<RawSearchResult>();
        var filteredOut = 0;
        var groupSet = userGroups is { Count: > 0 }
            ? new HashSet<string>(userGroups, StringComparer.OrdinalIgnoreCase)
            : null;

        foreach (var result in results)
        {
            // Public and Internal visibility: accessible to all authenticated users.
            if (!string.Equals(result.Visibility, "Restricted", StringComparison.OrdinalIgnoreCase))
            {
                filtered.Add(result);
                continue;
            }

            // Restricted visibility: user must be in at least one allowed group.
            if (groupSet is not null && result.AllowedGroups.Any(g => groupSet.Contains(g)))
            {
                filtered.Add(result);
            }
            else
            {
                filteredOut++;
            }
        }

        return (filtered, filteredOut);
    }

    private static string GetStringValue(SearchDocument doc, string key) =>
        doc.TryGetValue(key, out var val) && val is string s ? s : string.Empty;

    private static string? GetStringOrNull(SearchDocument doc, string key) =>
        doc.TryGetValue(key, out var val) && val is string s && !string.IsNullOrEmpty(s) ? s : null;

    private static IReadOnlyList<string> GetStringList(SearchDocument doc, string key)
    {
        if (!doc.TryGetValue(key, out var val)) return [];
        if (val is IEnumerable<string> strings) return strings.ToList();
        if (val is IEnumerable<object> objects) return objects.Select(o => o?.ToString() ?? "").ToList();
        return [];
    }

    /// <summary>Escapes single quotes in OData filter values.</summary>
    internal static string EscapeODataValue(string value) =>
        value.Replace("'", "''");
}

/// <summary>
/// Internal representation of a search result before ACL filtering and DTO mapping.
/// </summary>
internal sealed class RawSearchResult
{
    public required string ChunkId { get; init; }
    public required string EvidenceId { get; init; }
    public required string ChunkText { get; init; }
    public string? ChunkContext { get; init; }
    public required string Title { get; init; }
    public required string SourceUrl { get; init; }
    public required string SourceSystem { get; init; }
    public required string SourceType { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public string? ProductArea { get; init; }
    public required string AccessLabel { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public required string Visibility { get; init; }
    public IReadOnlyList<string> AllowedGroups { get; init; } = [];
    public double Score { get; init; }
    public double? SemanticScore { get; init; }
}
