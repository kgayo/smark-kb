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
/// Fused retrieval service (P1-004): queries both Evidence and Pattern indexes in parallel,
/// applies trust_level/recency/authority boosts, enforces diversity constraints,
/// and merges into a single ranked result set with ACL security trimming.
/// Replaces AzureSearchRetrievalService when pattern fusion is enabled.
/// </summary>
public sealed class FusedRetrievalService : IRetrievalService
{
    private readonly SearchIndexClient _indexClient;
    private readonly SearchServiceSettings _searchSettings;
    private readonly RetrievalSettings _retrievalSettings;
    private readonly ILogger<FusedRetrievalService> _logger;

    public FusedRetrievalService(
        SearchIndexClient indexClient,
        SearchServiceSettings searchSettings,
        RetrievalSettings retrievalSettings,
        ILogger<FusedRetrievalService> logger)
    {
        _indexClient = indexClient;
        _searchSettings = searchSettings;
        _retrievalSettings = retrievalSettings;
        _logger = logger;
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
            "Fused retrieval started. TraceId={TraceId} TenantId={TenantId} TopK={TopK} PatternTopK={PatternTopK} " +
            "FusionEnabled={FusionEnabled} SemanticRerank={SemanticRerank} HasFilters={HasFilters}",
            traceId, tenantId, _retrievalSettings.TopK, _retrievalSettings.PatternTopK,
            _retrievalSettings.EnablePatternFusion, _retrievalSettings.EnableSemanticReranking,
            filters is not null && !filters.IsEmpty);

        // Query Evidence and Pattern indexes in parallel.
        var evidenceTask = SearchEvidenceIndexAsync(tenantId, query, queryEmbedding, filters, cancellationToken);
        var patternTask = _retrievalSettings.EnablePatternFusion
            ? SearchPatternIndexAsync(tenantId, query, queryEmbedding, filters, cancellationToken)
            : Task.FromResult<List<RankedResult>>(new());

        List<RankedResult> evidenceResults;
        List<RankedResult> patternResults;

        try
        {
            await Task.WhenAll(evidenceTask, patternTask);
            evidenceResults = await evidenceTask;
            patternResults = await patternTask;
        }
        catch (Exception ex) when (ex is AggregateException or RequestFailedException)
        {
            // Salvage partial results: .Result is safe here because IsCompletedSuccessfully guarantees
            // the task finished without faulting or cancellation, so .Result will not block or throw.
            evidenceResults = evidenceTask.IsCompletedSuccessfully ? evidenceTask.Result : [];
            patternResults = patternTask.IsCompletedSuccessfully ? patternTask.Result : [];

            if (evidenceResults.Count == 0 && patternResults.Count == 0)
            {
                _logger.LogError(ex,
                    "Both Evidence and Pattern search queries failed. TraceId={TraceId} TenantId={TenantId}",
                    traceId, tenantId);

                return new RetrievalResult
                {
                    Chunks = [],
                    AclFilteredOutCount = 0,
                    HasEvidence = false,
                    TraceId = traceId,
                    PatternCount = 0,
                };
            }

            _logger.LogWarning(ex,
                "Partial retrieval failure. TraceId={TraceId} EvidenceCount={EvidenceCount} PatternCount={PatternCount}",
                traceId, evidenceResults.Count, patternResults.Count);
        }

        // Apply boosts to all results.
        var now = DateTimeOffset.UtcNow;
        foreach (var r in evidenceResults)
            r.BoostedScore = ApplyRecencyBoost(r.Score, r.UpdatedAt, now);

        foreach (var r in patternResults)
        {
            var boosted = r.Score;
            boosted *= GetTrustBoost(r.TrustLevel);
            boosted = ApplyRecencyBoost(boosted, r.UpdatedAt, now);
            boosted *= _retrievalSettings.PatternAuthorityBoost;
            r.BoostedScore = boosted;
        }

        // Merge all results.
        var allResults = new List<RankedResult>(evidenceResults.Count + patternResults.Count);
        allResults.AddRange(evidenceResults);
        allResults.AddRange(patternResults);

        // Apply ACL security trimming.
        var (aclFiltered, aclFilteredOutCount) = ApplyAclFilter(allResults, userGroups);

        // Sort by boosted score descending.
        aclFiltered.Sort((a, b) => b.BoostedScore.CompareTo(a.BoostedScore));

        // Apply diversity constraint: max N per source ID.
        var diversified = ApplyDiversityConstraint(aclFiltered, _retrievalSettings.DiversityMaxPerSource);

        // Take final top-k.
        var topResults = diversified
            .Take(_retrievalSettings.TopK)
            .ToList();

        // Map to RetrievedChunk DTOs.
        var chunks = topResults.Select(r => r.ToRetrievedChunk()).ToList();

        // Evaluate no-evidence condition (D-012).
        var aboveThresholdCount = chunks.Count(c =>
            c.BoostedScore >= _retrievalSettings.NoEvidenceScoreThreshold);
        var hasEvidence = aboveThresholdCount >= _retrievalSettings.NoEvidenceMinResults;

        var patternCount = chunks.Count(c => c.ResultSource == "Pattern");
        stopwatch.Stop();

        _logger.LogInformation(
            "Fused retrieval completed. TraceId={TraceId} TenantId={TenantId} " +
            "EvidenceRaw={EvidenceRaw} PatternRaw={PatternRaw} AclFilteredOut={AclFilteredOut} " +
            "Returned={Returned} PatternInResults={PatternInResults} " +
            "HasEvidence={HasEvidence} AboveThreshold={AboveThreshold} " +
            "DurationMs={DurationMs} TopIds={TopIds} TopScores={TopScores}",
            traceId, tenantId,
            evidenceResults.Count, patternResults.Count, aclFilteredOutCount,
            chunks.Count, patternCount,
            hasEvidence, aboveThresholdCount,
            stopwatch.ElapsedMilliseconds,
            string.Join(",", chunks.Take(5).Select(c => c.ChunkId)),
            string.Join(",", chunks.Take(5).Select(c => c.BoostedScore.ToString("F4"))));

        return new RetrievalResult
        {
            Chunks = chunks,
            AclFilteredOutCount = aclFilteredOutCount,
            HasEvidence = hasEvidence,
            TraceId = traceId,
            PatternCount = patternCount,
        };
    }

    private async Task<List<RankedResult>> SearchEvidenceIndexAsync(
        string tenantId, string query, float[] queryEmbedding, RetrievalFilter? filters, CancellationToken cancellationToken)
    {
        var searchClient = _indexClient.GetSearchClient(_searchSettings.EvidenceIndexName);
        var tenantFilter = $"{SearchFieldNames.TenantId} eq '{EscapeODataValue(tenantId)}'";
        var combinedFilter = ODataFilterBuilder.CombineFilters(tenantFilter, ODataFilterBuilder.BuildEvidenceFilter(filters));

        var options = new SearchOptions
        {
            Filter = combinedFilter,
            Size = _retrievalSettings.TopK * 2,
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

        if (_retrievalSettings.EnableSemanticReranking)
        {
            options.SemanticSearch = new SemanticSearchOptions
            {
                SemanticConfigurationName = SearchFieldNames.SemanticConfigName,
                QueryCaption = new QueryCaption(QueryCaptionType.Extractive),
            };
            options.QueryType = SearchQueryType.Semantic;
        }

        var results = new List<RankedResult>();

        try
        {
            var response = await searchClient.SearchAsync<SearchDocument>(query, options, cancellationToken);

            await foreach (var result in response.Value.GetResultsAsync())
            {
                var doc = result.Document;
                results.Add(new RankedResult
                {
                    Id = GetString(doc, SearchFieldNames.ChunkId),
                    SourceId = GetString(doc, SearchFieldNames.EvidenceId),
                    ChunkText = GetString(doc, SearchFieldNames.ChunkText),
                    ChunkContext = GetStringOrNull(doc, SearchFieldNames.ChunkContext),
                    Title = GetString(doc, SearchFieldNames.Title),
                    SourceUrl = GetString(doc, SearchFieldNames.SourceUrl),
                    SourceSystem = GetString(doc, SearchFieldNames.SourceSystem),
                    SourceType = GetString(doc, SearchFieldNames.SourceType),
                    UpdatedAt = doc.TryGetValue(SearchFieldNames.UpdatedAt, out var u) && u is DateTimeOffset dto
                        ? dto : DateTimeOffset.MinValue,
                    ProductArea = GetStringOrNull(doc, SearchFieldNames.ProductArea),
                    AccessLabel = GetString(doc, SearchFieldNames.AccessLabel),
                    Tags = GetStringList(doc, SearchFieldNames.Tags),
                    Visibility = GetString(doc, SearchFieldNames.Visibility),
                    AllowedGroups = GetStringList(doc, SearchFieldNames.AllowedGroups),
                    Score = result.Score ?? 0.0,
                    SemanticScore = result.SemanticSearch?.RerankerScore,
                    ResultSource = "Evidence",
                    TrustLevel = null,
                });
            }
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Evidence index search failed. Status={Status}", ex.Status);
            throw;
        }

        return results;
    }

    private async Task<List<RankedResult>> SearchPatternIndexAsync(
        string tenantId, string query, float[] queryEmbedding, RetrievalFilter? filters, CancellationToken cancellationToken)
    {
        var searchClient = _indexClient.GetSearchClient(_searchSettings.PatternIndexName);

        // Filter: tenant + exclude deprecated patterns from retrieval.
        var baseFilter = $"{PatternFieldNames.TenantId} eq '{EscapeODataValue(tenantId)}' " +
                         $"and {PatternFieldNames.TrustLevel} ne 'Deprecated'";
        var combinedFilter = ODataFilterBuilder.CombineFilters(baseFilter, ODataFilterBuilder.BuildPatternFilter(filters));

        var options = new SearchOptions
        {
            Filter = combinedFilter,
            Size = _retrievalSettings.PatternTopK * 2,
            Select =
            {
                PatternFieldNames.PatternId,
                PatternFieldNames.Title,
                PatternFieldNames.ProblemStatement,
                PatternFieldNames.Symptoms,
                PatternFieldNames.ResolutionSteps,
                PatternFieldNames.TenantId,
                PatternFieldNames.TrustLevel,
                PatternFieldNames.ProductArea,
                PatternFieldNames.UpdatedAt,
                PatternFieldNames.Confidence,
                PatternFieldNames.Tags,
                PatternFieldNames.Visibility,
                PatternFieldNames.AllowedGroups,
                PatternFieldNames.AccessLabel,
                PatternFieldNames.SourceUrl,
            },
            SearchFields =
            {
                PatternFieldNames.Title,
                PatternFieldNames.ProblemStatement,
                PatternFieldNames.Symptoms,
                PatternFieldNames.ResolutionSteps,
            },
            VectorSearch = new VectorSearchOptions
            {
                Queries =
                {
                    new VectorizedQuery(queryEmbedding)
                    {
                        KNearestNeighborsCount = _retrievalSettings.PatternTopK * 2,
                        Fields = { PatternFieldNames.EmbeddingVector },
                    },
                },
            },
        };

        if (_retrievalSettings.EnableSemanticReranking)
        {
            options.SemanticSearch = new SemanticSearchOptions
            {
                SemanticConfigurationName = PatternFieldNames.SemanticConfigName,
                QueryCaption = new QueryCaption(QueryCaptionType.Extractive),
            };
            options.QueryType = SearchQueryType.Semantic;
        }

        var results = new List<RankedResult>();

        try
        {
            var response = await searchClient.SearchAsync<SearchDocument>(query, options, cancellationToken);

            await foreach (var result in response.Value.GetResultsAsync())
            {
                var doc = result.Document;
                var problemStatement = GetString(doc, PatternFieldNames.ProblemStatement);
                var resolutionSteps = GetString(doc, PatternFieldNames.ResolutionSteps);
                var symptoms = GetString(doc, PatternFieldNames.Symptoms);

                // Build composite chunk text from pattern fields for the orchestrator.
                var chunkText = $"{problemStatement}\n\n## Resolution Steps\n{resolutionSteps}";
                var chunkContext = !string.IsNullOrEmpty(symptoms) ? $"Symptoms: {symptoms}" : null;

                var patternId = GetString(doc, PatternFieldNames.PatternId);

                results.Add(new RankedResult
                {
                    Id = patternId,
                    SourceId = patternId,
                    ChunkText = chunkText,
                    ChunkContext = chunkContext,
                    Title = GetString(doc, PatternFieldNames.Title),
                    SourceUrl = GetString(doc, PatternFieldNames.SourceUrl),
                    SourceSystem = "Pattern",
                    SourceType = "CasePattern",
                    UpdatedAt = doc.TryGetValue(PatternFieldNames.UpdatedAt, out var u) && u is DateTimeOffset dto
                        ? dto : DateTimeOffset.MinValue,
                    ProductArea = GetStringOrNull(doc, PatternFieldNames.ProductArea),
                    AccessLabel = GetString(doc, PatternFieldNames.AccessLabel),
                    Tags = GetStringList(doc, PatternFieldNames.Tags),
                    Visibility = GetString(doc, PatternFieldNames.Visibility),
                    AllowedGroups = GetStringList(doc, PatternFieldNames.AllowedGroups),
                    Score = result.Score ?? 0.0,
                    SemanticScore = result.SemanticSearch?.RerankerScore,
                    ResultSource = "Pattern",
                    TrustLevel = GetStringOrNull(doc, PatternFieldNames.TrustLevel),
                });
            }
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Pattern index search failed. Status={Status}", ex.Status);
            throw;
        }

        return results;
    }

    /// <summary>
    /// Applies trust level boost multiplier based on pattern governance state.
    /// Evidence results (trustLevel=null) get no boost (1.0x).
    /// </summary>
    internal float GetTrustBoost(string? trustLevel)
    {
        if (trustLevel is null) return 1.0f;

        return trustLevel.ToLowerInvariant() switch
        {
            "approved" => _retrievalSettings.TrustBoostApproved,
            "reviewed" => _retrievalSettings.TrustBoostReviewed,
            "draft" => _retrievalSettings.TrustBoostDraft,
            "deprecated" => _retrievalSettings.TrustBoostDeprecated,
            _ => 1.0f,
        };
    }

    /// <summary>
    /// Applies recency boost: recent (<=30d) gets boost, mid-range (30-90d) is neutral,
    /// old (>90d) gets penalty.
    /// </summary>
    internal double ApplyRecencyBoost(double score, DateTimeOffset updatedAt, DateTimeOffset now)
    {
        var age = now - updatedAt;
        if (age.TotalDays <= 30)
            return score * _retrievalSettings.RecencyBoostRecent;
        if (age.TotalDays > 90)
            return score * _retrievalSettings.RecencyBoostOld;
        return score; // 30-90 days: neutral (1.0x)
    }

    /// <summary>
    /// Applies ACL security trimming: restricted documents are only returned if user is in allowed_groups.
    /// Internal and public documents pass through for all authenticated users.
    /// </summary>
    internal static (List<RankedResult> Filtered, int FilteredOutCount) ApplyAclFilter(
        List<RankedResult> results,
        IReadOnlyList<string>? userGroups)
    {
        var filtered = new List<RankedResult>();
        var filteredOut = 0;
        var groupSet = userGroups is { Count: > 0 }
            ? new HashSet<string>(userGroups, StringComparer.OrdinalIgnoreCase)
            : null;

        foreach (var result in results)
        {
            if (!string.Equals(result.Visibility, "Restricted", StringComparison.OrdinalIgnoreCase))
            {
                filtered.Add(result);
                continue;
            }

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

    /// <summary>
    /// Enforces diversity constraint: max N results from the same source ID.
    /// Prevents one evidence record or pattern from dominating the result set.
    /// </summary>
    internal static List<RankedResult> ApplyDiversityConstraint(
        List<RankedResult> sortedResults,
        int maxPerSource)
    {
        var sourceCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var diversified = new List<RankedResult>();

        foreach (var result in sortedResults)
        {
            sourceCounts.TryGetValue(result.SourceId, out var count);
            if (count < maxPerSource)
            {
                diversified.Add(result);
                sourceCounts[result.SourceId] = count + 1;
            }
        }

        return diversified;
    }

    private static string GetString(SearchDocument doc, string key) =>
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

    internal static string EscapeODataValue(string value) =>
        value.Replace("'", "''");
}

/// <summary>
/// Internal representation of a search result from either index before ACL filtering and DTO mapping.
/// </summary>
internal sealed class RankedResult
{
    public required string Id { get; init; }
    public required string SourceId { get; init; }
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
    public required string ResultSource { get; init; }
    public string? TrustLevel { get; init; }
    public double BoostedScore { get; set; }

    public RetrievedChunk ToRetrievedChunk() => new()
    {
        ChunkId = Id,
        EvidenceId = SourceId,
        ChunkText = ChunkText,
        ChunkContext = ChunkContext,
        Title = Title,
        SourceUrl = SourceUrl,
        SourceSystem = SourceSystem,
        SourceType = SourceType,
        UpdatedAt = UpdatedAt,
        ProductArea = ProductArea,
        AccessLabel = AccessLabel,
        Tags = Tags,
        Visibility = Visibility,
        AllowedGroups = AllowedGroups,
        RrfScore = Score,
        SemanticScore = SemanticScore,
        ResultSource = ResultSource,
        TrustLevel = TrustLevel,
        BoostedScore = BoostedScore,
    };
}
