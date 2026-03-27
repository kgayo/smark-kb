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
/// Azure AI Search implementation of IIndexingService.
/// Manages the Evidence index schema and indexes evidence chunks with vector embeddings.
/// </summary>
public sealed class AzureSearchIndexingService : IIndexingService
{
    private readonly SearchIndexClient _indexClient;
    private readonly SearchServiceSettings _settings;
    private readonly ILogger<AzureSearchIndexingService> _logger;

    public AzureSearchIndexingService(
        SearchIndexClient indexClient,
        SearchServiceSettings settings,
        ILogger<AzureSearchIndexingService> logger)
    {
        _indexClient = indexClient;
        _settings = settings;
        _logger = logger;
    }

    public async Task EnsureIndexAsync(CancellationToken cancellationToken = default)
    {
        var index = BuildIndexDefinition();

        _logger.LogInformation("Ensuring Evidence index '{IndexName}' exists with vector profile and semantic config.",
            _settings.EvidenceIndexName);

        await _indexClient.CreateOrUpdateIndexAsync(index, cancellationToken: cancellationToken);

        _logger.LogInformation("Evidence index '{IndexName}' is ready.", _settings.EvidenceIndexName);
    }

    public async Task<IndexingResult> IndexChunksAsync(
        IReadOnlyList<EvidenceChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        if (chunks.Count == 0)
            return new IndexingResult(0, 0, []);

        var searchClient = _indexClient.GetSearchClient(_settings.EvidenceIndexName);
        var succeeded = 0;
        var failed = 0;
        var failedIds = new List<string>();

        // Process in batches.
        for (var i = 0; i < chunks.Count; i += _settings.IndexBatchSize)
        {
            var batch = chunks.Skip(i).Take(_settings.IndexBatchSize).ToList();
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
                            "Failed to index chunk {ChunkId}: {Status} {Message}",
                            result.Key, result.Status, result.ErrorMessage);
                    }
                }
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError(ex,
                    "Batch indexing failed for {Count} chunks starting at offset {Offset}.",
                    batch.Count, i);
                failed += batch.Count;
                failedIds.AddRange(batch.Select(c => c.ChunkId));
            }
        }

        _logger.LogInformation(
            "Indexed {Succeeded} chunks, {Failed} failed out of {Total} total.",
            succeeded, failed, chunks.Count);

        return new IndexingResult(succeeded, failed, failedIds);
    }

    public async Task<int> DeleteChunksAsync(
        IReadOnlyList<string> chunkIds,
        CancellationToken cancellationToken = default)
    {
        if (chunkIds.Count == 0) return 0;

        var searchClient = _indexClient.GetSearchClient(_settings.EvidenceIndexName);
        var deleted = 0;

        for (var i = 0; i < chunkIds.Count; i += _settings.IndexBatchSize)
        {
            var batch = chunkIds.Skip(i).Take(_settings.IndexBatchSize).ToList();

            try
            {
                var response = await searchClient.DeleteDocumentsAsync(
                    SearchFieldNames.ChunkId, batch, cancellationToken: cancellationToken);

                deleted += response.Value.Results.Count(r => r.Succeeded);
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError(ex, "Failed to delete {Count} chunks from index.", batch.Count);
            }
        }

        return deleted;
    }

    public SearchIndex BuildIndexDefinition()
    {
        var index = new SearchIndex(_settings.EvidenceIndexName)
        {
            Fields =
            {
                // Key
                new SimpleField(SearchFieldNames.ChunkId, SearchFieldDataType.String) { IsKey = true, IsFilterable = true },

                // Searchable text
                new SearchableField(SearchFieldNames.ChunkText) { AnalyzerName = LexicalAnalyzerName.EnMicrosoft },
                new SearchableField(SearchFieldNames.ChunkContext) { AnalyzerName = LexicalAnalyzerName.EnMicrosoft },
                new SearchableField(SearchFieldNames.Title) { AnalyzerName = LexicalAnalyzerName.EnMicrosoft, IsFilterable = true, IsSortable = true },

                // Vector
                new VectorSearchField(SearchFieldNames.EmbeddingVector, SearchFieldNames.EmbeddingDimensions, SearchFieldNames.VectorProfileName),

                // Filterable metadata
                new SimpleField(SearchFieldNames.TenantId, SearchFieldDataType.String) { IsFilterable = true },
                new SimpleField(SearchFieldNames.EvidenceId, SearchFieldDataType.String) { IsFilterable = true },
                new SimpleField(SearchFieldNames.SourceSystem, SearchFieldDataType.String) { IsFilterable = true, IsFacetable = true },
                new SimpleField(SearchFieldNames.SourceType, SearchFieldDataType.String) { IsFilterable = true, IsFacetable = true },
                new SimpleField(SearchFieldNames.Status, SearchFieldDataType.String) { IsFilterable = true },
                new SimpleField(SearchFieldNames.UpdatedAt, SearchFieldDataType.DateTimeOffset) { IsFilterable = true, IsSortable = true },
                new SimpleField(SearchFieldNames.ProductArea, SearchFieldDataType.String) { IsFilterable = true, IsFacetable = true },
                new SimpleField(SearchFieldNames.Tags, SearchFieldDataType.Collection(SearchFieldDataType.String)) { IsFilterable = true, IsFacetable = true },

                // ACL security trimming
                new SimpleField(SearchFieldNames.Visibility, SearchFieldDataType.String) { IsFilterable = true },
                new SimpleField(SearchFieldNames.AllowedGroups, SearchFieldDataType.Collection(SearchFieldDataType.String)) { IsFilterable = true },
                new SimpleField(SearchFieldNames.AccessLabel, SearchFieldDataType.String) { IsFilterable = true },

                // Source linkage
                new SimpleField(SearchFieldNames.SourceUrl, SearchFieldDataType.String) { IsFilterable = false },
            },

            VectorSearch = new VectorSearch
            {
                Profiles =
                {
                    new VectorSearchProfile(SearchFieldNames.VectorProfileName, "evidence-hnsw-config"),
                },
                Algorithms =
                {
                    new HnswAlgorithmConfiguration("evidence-hnsw-config")
                    {
                        Parameters = VectorSearchDefaults.CreateHnswParameters(),
                    },
                },
            },

            SemanticSearch = new SemanticSearch
            {
                Configurations =
                {
                    new SemanticConfiguration(SearchFieldNames.SemanticConfigName, new SemanticPrioritizedFields
                    {
                        TitleField = new SemanticField(SearchFieldNames.Title),
                        ContentFields =
                        {
                            new SemanticField(SearchFieldNames.ChunkText),
                            new SemanticField(SearchFieldNames.ChunkContext),
                        },
                        KeywordsFields =
                        {
                            new SemanticField(SearchFieldNames.Tags),
                        },
                    }),
                },
            },
        };

        return index;
    }

    public static SearchDocument ToSearchDocument(EvidenceChunk chunk)
    {
        var doc = new SearchDocument
        {
            [SearchFieldNames.ChunkId] = chunk.ChunkId,
            [SearchFieldNames.ChunkText] = chunk.ChunkText,
            [SearchFieldNames.ChunkContext] = chunk.ChunkContext ?? string.Empty,
            [SearchFieldNames.Title] = chunk.Title,
            [SearchFieldNames.EmbeddingVector] = chunk.EmbeddingVector,
            [SearchFieldNames.TenantId] = chunk.TenantId,
            [SearchFieldNames.EvidenceId] = chunk.EvidenceId,
            [SearchFieldNames.SourceSystem] = chunk.SourceSystem.ToString(),
            [SearchFieldNames.SourceType] = chunk.SourceType.ToString(),
            [SearchFieldNames.Status] = chunk.Status.ToString(),
            [SearchFieldNames.UpdatedAt] = chunk.UpdatedAt,
            [SearchFieldNames.ProductArea] = chunk.ProductArea ?? string.Empty,
            [SearchFieldNames.Tags] = chunk.Tags.ToList(),
            [SearchFieldNames.Visibility] = chunk.Visibility.ToString(),
            [SearchFieldNames.AllowedGroups] = chunk.AllowedGroups.ToList(),
            [SearchFieldNames.AccessLabel] = chunk.AccessLabel,
            [SearchFieldNames.SourceUrl] = chunk.SourceUrl,
        };

        return doc;
    }
}
