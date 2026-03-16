using Azure.Search.Documents.Indexes.Models;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Search;
using SmartKb.Contracts.Services;

namespace SmartKb.Contracts.Tests;

public class AzureSearchIndexingServiceTests
{
    private readonly SearchServiceSettings _settings = new()
    {
        Endpoint = "https://test.search.windows.net",
        EvidenceIndexName = "evidence-test",
        IndexBatchSize = 50,
    };

    private readonly ILogger<AzureSearchIndexingService> _logger =
        new LoggerFactory().CreateLogger<AzureSearchIndexingService>();

    [Fact]
    public void BuildIndexDefinition_HasCorrectName()
    {
        var service = new AzureSearchIndexingService(null!, _settings, _logger);
        var index = service.BuildIndexDefinition();

        Assert.Equal("evidence-test", index.Name);
    }

    [Fact]
    public void BuildIndexDefinition_HasAllExpectedFields()
    {
        var service = new AzureSearchIndexingService(null!, _settings, _logger);
        var index = service.BuildIndexDefinition();

        var fieldNames = index.Fields.Select(f => f.Name).ToList();

        // Key
        Assert.Contains(SearchFieldNames.ChunkId, fieldNames);

        // Searchable text
        Assert.Contains(SearchFieldNames.ChunkText, fieldNames);
        Assert.Contains(SearchFieldNames.ChunkContext, fieldNames);
        Assert.Contains(SearchFieldNames.Title, fieldNames);

        // Vector
        Assert.Contains(SearchFieldNames.EmbeddingVector, fieldNames);

        // Filterable metadata
        Assert.Contains(SearchFieldNames.TenantId, fieldNames);
        Assert.Contains(SearchFieldNames.EvidenceId, fieldNames);
        Assert.Contains(SearchFieldNames.SourceSystem, fieldNames);
        Assert.Contains(SearchFieldNames.SourceType, fieldNames);
        Assert.Contains(SearchFieldNames.Status, fieldNames);
        Assert.Contains(SearchFieldNames.UpdatedAt, fieldNames);
        Assert.Contains(SearchFieldNames.ProductArea, fieldNames);
        Assert.Contains(SearchFieldNames.Tags, fieldNames);

        // ACL
        Assert.Contains(SearchFieldNames.Visibility, fieldNames);
        Assert.Contains(SearchFieldNames.AllowedGroups, fieldNames);
        Assert.Contains(SearchFieldNames.AccessLabel, fieldNames);

        // Source linkage
        Assert.Contains(SearchFieldNames.SourceUrl, fieldNames);
    }

    [Fact]
    public void BuildIndexDefinition_ChunkId_IsKey()
    {
        var service = new AzureSearchIndexingService(null!, _settings, _logger);
        var index = service.BuildIndexDefinition();

        var keyField = index.Fields.First(f => f.Name == SearchFieldNames.ChunkId);
        Assert.True(keyField.IsKey);
    }

    [Fact]
    public void BuildIndexDefinition_HasVectorSearchProfile()
    {
        var service = new AzureSearchIndexingService(null!, _settings, _logger);
        var index = service.BuildIndexDefinition();

        Assert.NotNull(index.VectorSearch);
        Assert.Single(index.VectorSearch.Profiles);
        Assert.Equal(SearchFieldNames.VectorProfileName, index.VectorSearch.Profiles[0].Name);
    }

    [Fact]
    public void BuildIndexDefinition_HasHnswAlgorithm_WithCosineMetric()
    {
        var service = new AzureSearchIndexingService(null!, _settings, _logger);
        var index = service.BuildIndexDefinition();

        Assert.Single(index.VectorSearch.Algorithms);
        var algo = index.VectorSearch.Algorithms[0] as HnswAlgorithmConfiguration;
        Assert.NotNull(algo);
        Assert.Equal(VectorSearchAlgorithmMetric.Cosine, algo.Parameters.Metric);
    }

    [Fact]
    public void BuildIndexDefinition_HasSemanticConfiguration()
    {
        var service = new AzureSearchIndexingService(null!, _settings, _logger);
        var index = service.BuildIndexDefinition();

        Assert.NotNull(index.SemanticSearch);
        Assert.Single(index.SemanticSearch.Configurations);
        var config = index.SemanticSearch.Configurations[0];
        Assert.Equal(SearchFieldNames.SemanticConfigName, config.Name);
        Assert.Equal(SearchFieldNames.Title, config.PrioritizedFields.TitleField.FieldName);
    }

    [Fact]
    public void BuildIndexDefinition_SemanticConfig_HasContentFields()
    {
        var service = new AzureSearchIndexingService(null!, _settings, _logger);
        var index = service.BuildIndexDefinition();

        var config = index.SemanticSearch.Configurations[0];
        var contentFieldNames = config.PrioritizedFields.ContentFields.Select(f => f.FieldName).ToList();
        Assert.Contains(SearchFieldNames.ChunkText, contentFieldNames);
        Assert.Contains(SearchFieldNames.ChunkContext, contentFieldNames);
    }

    [Fact]
    public void BuildIndexDefinition_SemanticConfig_HasKeywordsField()
    {
        var service = new AzureSearchIndexingService(null!, _settings, _logger);
        var index = service.BuildIndexDefinition();

        var config = index.SemanticSearch.Configurations[0];
        var keywordsFieldNames = config.PrioritizedFields.KeywordsFields.Select(f => f.FieldName).ToList();
        Assert.Contains(SearchFieldNames.Tags, keywordsFieldNames);
    }

    [Fact]
    public void BuildIndexDefinition_TenantId_IsFilterable()
    {
        var service = new AzureSearchIndexingService(null!, _settings, _logger);
        var index = service.BuildIndexDefinition();

        var field = index.Fields.First(f => f.Name == SearchFieldNames.TenantId);
        Assert.True(field.IsFilterable);
    }

    [Fact]
    public void BuildIndexDefinition_AclFields_AreFilterable()
    {
        var service = new AzureSearchIndexingService(null!, _settings, _logger);
        var index = service.BuildIndexDefinition();

        foreach (var name in new[] { SearchFieldNames.Visibility, SearchFieldNames.AllowedGroups, SearchFieldNames.AccessLabel })
        {
            var field = index.Fields.First(f => f.Name == name);
            Assert.True(field.IsFilterable, $"Field {name} should be filterable for ACL trimming.");
        }
    }

    [Fact]
    public void BuildIndexDefinition_SourceSystem_IsFacetable()
    {
        var service = new AzureSearchIndexingService(null!, _settings, _logger);
        var index = service.BuildIndexDefinition();

        var field = index.Fields.First(f => f.Name == SearchFieldNames.SourceSystem);
        Assert.True(field.IsFacetable);
    }

    [Fact]
    public void ToSearchDocument_MapsAllFields()
    {
        var chunk = CreateTestChunk();
        var doc = AzureSearchIndexingService.ToSearchDocument(chunk);

        Assert.Equal(chunk.ChunkId, doc[SearchFieldNames.ChunkId]);
        Assert.Equal(chunk.ChunkText, doc[SearchFieldNames.ChunkText]);
        Assert.Equal(chunk.ChunkContext, doc[SearchFieldNames.ChunkContext]);
        Assert.Equal(chunk.Title, doc[SearchFieldNames.Title]);
        Assert.Equal(chunk.EmbeddingVector, doc[SearchFieldNames.EmbeddingVector]);
        Assert.Equal(chunk.TenantId, doc[SearchFieldNames.TenantId]);
        Assert.Equal(chunk.EvidenceId, doc[SearchFieldNames.EvidenceId]);
        Assert.Equal("AzureDevOps", doc[SearchFieldNames.SourceSystem]);
        Assert.Equal("WorkItem", doc[SearchFieldNames.SourceType]);
        Assert.Equal("Open", doc[SearchFieldNames.Status]);
        Assert.Equal(chunk.UpdatedAt, doc[SearchFieldNames.UpdatedAt]);
        Assert.Equal(chunk.ProductArea ?? string.Empty, doc[SearchFieldNames.ProductArea]);
        Assert.Equal(chunk.SourceUrl, doc[SearchFieldNames.SourceUrl]);
        Assert.Equal(chunk.AccessLabel, doc[SearchFieldNames.AccessLabel]);
        Assert.Equal("Internal", doc[SearchFieldNames.Visibility]);
    }

    [Fact]
    public void ToSearchDocument_MapsTagsAsCollection()
    {
        var chunk = CreateTestChunk() with { Tags = ["tag1", "tag2"] };
        var doc = AzureSearchIndexingService.ToSearchDocument(chunk);

        var tags = doc[SearchFieldNames.Tags] as List<string>;
        Assert.NotNull(tags);
        Assert.Equal(2, tags.Count);
        Assert.Contains("tag1", tags);
        Assert.Contains("tag2", tags);
    }

    [Fact]
    public void ToSearchDocument_MapsAllowedGroupsAsCollection()
    {
        var chunk = CreateTestChunk() with { AllowedGroups = ["group-a", "group-b"] };
        var doc = AzureSearchIndexingService.ToSearchDocument(chunk);

        var groups = doc[SearchFieldNames.AllowedGroups] as List<string>;
        Assert.NotNull(groups);
        Assert.Equal(2, groups.Count);
        Assert.Contains("group-a", groups);
    }

    [Fact]
    public void ToSearchDocument_HandlesNullChunkContext()
    {
        var chunk = CreateTestChunk() with { ChunkContext = null };
        var doc = AzureSearchIndexingService.ToSearchDocument(chunk);

        Assert.Equal(string.Empty, doc[SearchFieldNames.ChunkContext]);
    }

    [Fact]
    public void ToSearchDocument_HandlesNullProductArea()
    {
        var chunk = CreateTestChunk() with { ProductArea = null };
        var doc = AzureSearchIndexingService.ToSearchDocument(chunk);

        Assert.Equal(string.Empty, doc[SearchFieldNames.ProductArea]);
    }

    [Fact]
    public async Task IndexChunksAsync_ReturnsEmpty_WhenNoChunks()
    {
        var service = new AzureSearchIndexingService(null!, _settings, _logger);
        var result = await service.IndexChunksAsync([]);

        Assert.Equal(0, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.Empty(result.FailedChunkIds);
    }

    [Fact]
    public async Task DeleteChunksAsync_ReturnsZero_WhenNoIds()
    {
        var service = new AzureSearchIndexingService(null!, _settings, _logger);
        var result = await service.DeleteChunksAsync([]);

        Assert.Equal(0, result);
    }

    private static EvidenceChunk CreateTestChunk() => new()
    {
        ChunkId = "ev-123_chunk_0",
        EvidenceId = "ev-123",
        TenantId = "tenant-1",
        ChunkIndex = 0,
        ChunkText = "This is the chunk text content.",
        ChunkContext = "# Root > Section 1",
        EmbeddingVector = Enumerable.Range(0, SearchFieldNames.EmbeddingDimensions).Select(i => (float)i / 1536f).ToArray(),
        SourceSystem = ConnectorType.AzureDevOps,
        SourceType = SourceType.WorkItem,
        Status = EvidenceStatus.Open,
        UpdatedAt = DateTimeOffset.UtcNow,
        ProductArea = "Platform",
        Tags = ["bug", "critical"],
        Visibility = AccessVisibility.Internal,
        AllowedGroups = [],
        AccessLabel = "Internal",
        Title = "Fix login timeout",
        SourceUrl = "https://dev.azure.com/test/proj/_workitems/edit/123",
    };
}
