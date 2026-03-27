using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Search;

namespace SmartKb.Contracts.Services;

/// <summary>
/// Azure AI Search implementation of IPatternIndexingService.
/// Manages the Pattern index schema and indexes case patterns with vector embeddings.
/// </summary>
public sealed class AzureSearchPatternIndexingService : IPatternIndexingService
{
    private readonly SearchIndexClient _indexClient;
    private readonly SearchServiceSettings _settings;
    private readonly ILogger<AzureSearchPatternIndexingService> _logger;

    public AzureSearchPatternIndexingService(
        SearchIndexClient indexClient,
        SearchServiceSettings settings,
        ILogger<AzureSearchPatternIndexingService> logger)
    {
        _indexClient = indexClient;
        _settings = settings;
        _logger = logger;
    }

    public async Task EnsureIndexAsync(CancellationToken cancellationToken = default)
    {
        var index = BuildIndexDefinition();

        _logger.LogInformation("Ensuring Pattern index '{IndexName}' exists with vector profile and semantic config.",
            _settings.PatternIndexName);

        await _indexClient.CreateOrUpdateIndexAsync(index, cancellationToken: cancellationToken);

        _logger.LogInformation("Pattern index '{IndexName}' is ready.", _settings.PatternIndexName);
    }

    public async Task<IndexingResult> IndexPatternsAsync(
        IReadOnlyList<CasePattern> patterns,
        CancellationToken cancellationToken = default)
    {
        if (patterns.Count == 0)
            return new IndexingResult(0, 0, []);

        var searchClient = _indexClient.GetSearchClient(_settings.PatternIndexName);
        var succeeded = 0;
        var failed = 0;
        var failedIds = new List<string>();

        for (var i = 0; i < patterns.Count; i += _settings.IndexBatchSize)
        {
            var batch = patterns.Skip(i).Take(_settings.IndexBatchSize).ToList();
            var documents = batch.Select(ToSearchDocument).ToList();

            try
            {
                var response = await searchClient.MergeOrUploadDocumentsAsync(
                    documents, cancellationToken: cancellationToken);

                foreach (var result in response.Value.Results)
                {
                    if (result.Succeeded)
                    {
                        succeeded++;
                    }
                    else
                    {
                        failed++;
                        failedIds.Add(result.Key);
                        _logger.LogWarning(
                            "Failed to index pattern {PatternId}: {Status} {Message}",
                            result.Key, result.Status, result.ErrorMessage);
                    }
                }
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError(ex,
                    "Batch indexing failed for {Count} patterns starting at offset {Offset}.",
                    batch.Count, i);
                failed += batch.Count;
                failedIds.AddRange(batch.Select(p => p.PatternId));
            }
        }

        _logger.LogInformation(
            "Indexed {Succeeded} patterns, {Failed} failed out of {Total} total.",
            succeeded, failed, patterns.Count);

        return new IndexingResult(succeeded, failed, failedIds);
    }

    public async Task<int> DeletePatternsAsync(
        IReadOnlyList<string> patternIds,
        CancellationToken cancellationToken = default)
    {
        if (patternIds.Count == 0) return 0;

        var searchClient = _indexClient.GetSearchClient(_settings.PatternIndexName);
        var deleted = 0;

        for (var i = 0; i < patternIds.Count; i += _settings.IndexBatchSize)
        {
            var batch = patternIds.Skip(i).Take(_settings.IndexBatchSize).ToList();

            try
            {
                var response = await searchClient.DeleteDocumentsAsync(
                    PatternFieldNames.PatternId, batch, cancellationToken: cancellationToken);

                deleted += response.Value.Results.Count(r => r.Succeeded);
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError(ex, "Failed to delete {Count} patterns from index.", batch.Count);
            }
        }

        return deleted;
    }

    public SearchIndex BuildIndexDefinition()
    {
        var index = new SearchIndex(_settings.PatternIndexName)
        {
            Fields =
            {
                // Key
                new SimpleField(PatternFieldNames.PatternId, SearchFieldDataType.String) { IsKey = true, IsFilterable = true },

                // Searchable text
                new SearchableField(PatternFieldNames.Title) { AnalyzerName = LexicalAnalyzerName.EnMicrosoft, IsFilterable = true, IsSortable = true },
                new SearchableField(PatternFieldNames.ProblemStatement) { AnalyzerName = LexicalAnalyzerName.EnMicrosoft },
                new SearchableField(PatternFieldNames.RootCause) { AnalyzerName = LexicalAnalyzerName.EnMicrosoft },
                new SearchableField(PatternFieldNames.Symptoms) { AnalyzerName = LexicalAnalyzerName.EnMicrosoft },
                new SearchableField(PatternFieldNames.ResolutionSteps) { AnalyzerName = LexicalAnalyzerName.EnMicrosoft },

                // Vector
                new VectorSearchField(PatternFieldNames.EmbeddingVector, PatternFieldNames.EmbeddingDimensions, PatternFieldNames.VectorProfileName),

                // Filterable metadata
                new SimpleField(PatternFieldNames.TenantId, SearchFieldDataType.String) { IsFilterable = true },
                new SimpleField(PatternFieldNames.TrustLevel, SearchFieldDataType.String) { IsFilterable = true, IsFacetable = true },
                new SimpleField(PatternFieldNames.ProductArea, SearchFieldDataType.String) { IsFilterable = true, IsFacetable = true },
                new SimpleField(PatternFieldNames.UpdatedAt, SearchFieldDataType.DateTimeOffset) { IsFilterable = true, IsSortable = true },
                new SimpleField(PatternFieldNames.Confidence, SearchFieldDataType.Double) { IsFilterable = true, IsSortable = true },
                new SimpleField(PatternFieldNames.Tags, SearchFieldDataType.Collection(SearchFieldDataType.String)) { IsFilterable = true, IsFacetable = true },
                new SimpleField(PatternFieldNames.Version, SearchFieldDataType.Int32) { IsFilterable = true },

                // ACL security trimming
                new SimpleField(PatternFieldNames.Visibility, SearchFieldDataType.String) { IsFilterable = true },
                new SimpleField(PatternFieldNames.AllowedGroups, SearchFieldDataType.Collection(SearchFieldDataType.String)) { IsFilterable = true },
                new SimpleField(PatternFieldNames.AccessLabel, SearchFieldDataType.String) { IsFilterable = true },

                // Source linkage
                new SimpleField(PatternFieldNames.SourceUrl, SearchFieldDataType.String) { IsFilterable = false },
            },

            VectorSearch = new VectorSearch
            {
                Profiles =
                {
                    new VectorSearchProfile(PatternFieldNames.VectorProfileName, "pattern-hnsw-config"),
                },
                Algorithms =
                {
                    new HnswAlgorithmConfiguration("pattern-hnsw-config")
                    {
                        Parameters = VectorSearchDefaults.CreateHnswParameters(),
                    },
                },
            },

            SemanticSearch = new SemanticSearch
            {
                Configurations =
                {
                    new SemanticConfiguration(PatternFieldNames.SemanticConfigName, new SemanticPrioritizedFields
                    {
                        TitleField = new SemanticField(PatternFieldNames.Title),
                        ContentFields =
                        {
                            new SemanticField(PatternFieldNames.ProblemStatement),
                            new SemanticField(PatternFieldNames.RootCause),
                            new SemanticField(PatternFieldNames.ResolutionSteps),
                        },
                        KeywordsFields =
                        {
                            new SemanticField(PatternFieldNames.Tags),
                        },
                    }),
                },
            },
        };

        return index;
    }

    public static SearchDocument ToSearchDocument(CasePattern pattern)
    {
        return new SearchDocument
        {
            [PatternFieldNames.PatternId] = pattern.PatternId,
            [PatternFieldNames.Title] = pattern.Title,
            [PatternFieldNames.ProblemStatement] = pattern.ProblemStatement,
            [PatternFieldNames.RootCause] = pattern.RootCause ?? string.Empty,
            [PatternFieldNames.Symptoms] = string.Join("\n", pattern.Symptoms),
            [PatternFieldNames.ResolutionSteps] = string.Join("\n", pattern.ResolutionSteps),
            [PatternFieldNames.EmbeddingVector] = pattern.EmbeddingVector,
            [PatternFieldNames.TenantId] = pattern.TenantId,
            [PatternFieldNames.TrustLevel] = pattern.TrustLevel.ToString(),
            [PatternFieldNames.ProductArea] = pattern.ProductArea ?? string.Empty,
            [PatternFieldNames.UpdatedAt] = pattern.UpdatedAt,
            [PatternFieldNames.Confidence] = (double)pattern.Confidence,
            [PatternFieldNames.Tags] = pattern.Tags.ToList(),
            [PatternFieldNames.Version] = pattern.Version,
            [PatternFieldNames.Visibility] = pattern.Visibility.ToString(),
            [PatternFieldNames.AllowedGroups] = pattern.AllowedGroups.ToList(),
            [PatternFieldNames.AccessLabel] = pattern.AccessLabel,
            [PatternFieldNames.SourceUrl] = pattern.SourceUrl,
        };
    }
}
