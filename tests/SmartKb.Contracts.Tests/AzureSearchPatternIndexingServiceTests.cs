using Azure.Search.Documents.Indexes.Models;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Search;
using SmartKb.Contracts.Services;

namespace SmartKb.Contracts.Tests;

public class AzureSearchPatternIndexingServiceTests
{
    private readonly SearchServiceSettings _settings = new()
    {
        Endpoint = "https://test.search.windows.net",
        PatternIndexName = "patterns-test",
        IndexBatchSize = 50,
    };

    private readonly ILogger<AzureSearchPatternIndexingService> _logger =
        new LoggerFactory().CreateLogger<AzureSearchPatternIndexingService>();

    [Fact]
    public void BuildIndexDefinition_HasCorrectName()
    {
        var service = new AzureSearchPatternIndexingService(null!, _settings, _logger);
        var index = service.BuildIndexDefinition();

        Assert.Equal("patterns-test", index.Name);
    }

    [Fact]
    public void BuildIndexDefinition_HasAllExpectedFields()
    {
        var service = new AzureSearchPatternIndexingService(null!, _settings, _logger);
        var index = service.BuildIndexDefinition();

        var fieldNames = index.Fields.Select(f => f.Name).ToList();

        // Key
        Assert.Contains(PatternFieldNames.PatternId, fieldNames);

        // Searchable text
        Assert.Contains(PatternFieldNames.Title, fieldNames);
        Assert.Contains(PatternFieldNames.ProblemStatement, fieldNames);
        Assert.Contains(PatternFieldNames.RootCause, fieldNames);
        Assert.Contains(PatternFieldNames.Symptoms, fieldNames);
        Assert.Contains(PatternFieldNames.ResolutionSteps, fieldNames);

        // Vector
        Assert.Contains(PatternFieldNames.EmbeddingVector, fieldNames);

        // Filterable metadata
        Assert.Contains(PatternFieldNames.TenantId, fieldNames);
        Assert.Contains(PatternFieldNames.TrustLevel, fieldNames);
        Assert.Contains(PatternFieldNames.ProductArea, fieldNames);
        Assert.Contains(PatternFieldNames.UpdatedAt, fieldNames);
        Assert.Contains(PatternFieldNames.Confidence, fieldNames);
        Assert.Contains(PatternFieldNames.Tags, fieldNames);
        Assert.Contains(PatternFieldNames.Version, fieldNames);

        // ACL
        Assert.Contains(PatternFieldNames.Visibility, fieldNames);
        Assert.Contains(PatternFieldNames.AllowedGroups, fieldNames);
        Assert.Contains(PatternFieldNames.AccessLabel, fieldNames);

        // Source linkage
        Assert.Contains(PatternFieldNames.SourceUrl, fieldNames);
    }

    [Fact]
    public void BuildIndexDefinition_PatternId_IsKey()
    {
        var service = new AzureSearchPatternIndexingService(null!, _settings, _logger);
        var index = service.BuildIndexDefinition();

        var keyField = index.Fields.First(f => f.Name == PatternFieldNames.PatternId);
        Assert.True(keyField.IsKey);
        Assert.True(keyField.IsFilterable);
    }

    [Fact]
    public void BuildIndexDefinition_Title_IsSearchableAndFilterableAndSortable()
    {
        var service = new AzureSearchPatternIndexingService(null!, _settings, _logger);
        var index = service.BuildIndexDefinition();

        var field = index.Fields.First(f => f.Name == PatternFieldNames.Title);
        Assert.True(field.IsFilterable);
        Assert.True(field.IsSortable);
    }

    [Fact]
    public void BuildIndexDefinition_HasVectorSearchProfile()
    {
        var service = new AzureSearchPatternIndexingService(null!, _settings, _logger);
        var index = service.BuildIndexDefinition();

        Assert.NotNull(index.VectorSearch);
        Assert.Single(index.VectorSearch.Profiles);
        Assert.Equal(PatternFieldNames.VectorProfileName, index.VectorSearch.Profiles[0].Name);
    }

    [Fact]
    public void BuildIndexDefinition_HasHnswAlgorithm_WithCosineMetric()
    {
        var service = new AzureSearchPatternIndexingService(null!, _settings, _logger);
        var index = service.BuildIndexDefinition();

        Assert.Single(index.VectorSearch.Algorithms);
        var algo = index.VectorSearch.Algorithms[0] as HnswAlgorithmConfiguration;
        Assert.NotNull(algo);
        Assert.Equal(VectorSearchAlgorithmMetric.Cosine, algo.Parameters.Metric);
        Assert.Equal(4, algo.Parameters.M);
        Assert.Equal(400, algo.Parameters.EfConstruction);
        Assert.Equal(500, algo.Parameters.EfSearch);
    }

    [Fact]
    public void BuildIndexDefinition_HasSemanticConfiguration()
    {
        var service = new AzureSearchPatternIndexingService(null!, _settings, _logger);
        var index = service.BuildIndexDefinition();

        Assert.NotNull(index.SemanticSearch);
        Assert.Single(index.SemanticSearch.Configurations);
        var config = index.SemanticSearch.Configurations[0];
        Assert.Equal(PatternFieldNames.SemanticConfigName, config.Name);
        Assert.Equal(PatternFieldNames.Title, config.PrioritizedFields.TitleField.FieldName);
    }

    [Fact]
    public void BuildIndexDefinition_SemanticConfig_HasContentFields()
    {
        var service = new AzureSearchPatternIndexingService(null!, _settings, _logger);
        var index = service.BuildIndexDefinition();

        var config = index.SemanticSearch.Configurations[0];
        var contentFieldNames = config.PrioritizedFields.ContentFields.Select(f => f.FieldName).ToList();
        Assert.Contains(PatternFieldNames.ProblemStatement, contentFieldNames);
        Assert.Contains(PatternFieldNames.RootCause, contentFieldNames);
        Assert.Contains(PatternFieldNames.ResolutionSteps, contentFieldNames);
    }

    [Fact]
    public void BuildIndexDefinition_SemanticConfig_HasKeywordsField()
    {
        var service = new AzureSearchPatternIndexingService(null!, _settings, _logger);
        var index = service.BuildIndexDefinition();

        var config = index.SemanticSearch.Configurations[0];
        var keywordsFieldNames = config.PrioritizedFields.KeywordsFields.Select(f => f.FieldName).ToList();
        Assert.Contains(PatternFieldNames.Tags, keywordsFieldNames);
    }

    [Fact]
    public void BuildIndexDefinition_TenantId_IsFilterable()
    {
        var service = new AzureSearchPatternIndexingService(null!, _settings, _logger);
        var index = service.BuildIndexDefinition();

        var field = index.Fields.First(f => f.Name == PatternFieldNames.TenantId);
        Assert.True(field.IsFilterable);
    }

    [Fact]
    public void BuildIndexDefinition_AclFields_AreFilterable()
    {
        var service = new AzureSearchPatternIndexingService(null!, _settings, _logger);
        var index = service.BuildIndexDefinition();

        foreach (var name in new[] { PatternFieldNames.Visibility, PatternFieldNames.AllowedGroups, PatternFieldNames.AccessLabel })
        {
            var field = index.Fields.First(f => f.Name == name);
            Assert.True(field.IsFilterable, $"Field {name} should be filterable for ACL trimming.");
        }
    }

    [Fact]
    public void BuildIndexDefinition_TrustLevel_IsFacetable()
    {
        var service = new AzureSearchPatternIndexingService(null!, _settings, _logger);
        var index = service.BuildIndexDefinition();

        var field = index.Fields.First(f => f.Name == PatternFieldNames.TrustLevel);
        Assert.True(field.IsFacetable);
        Assert.True(field.IsFilterable);
    }

    [Fact]
    public void BuildIndexDefinition_Confidence_IsFilterableAndSortable()
    {
        var service = new AzureSearchPatternIndexingService(null!, _settings, _logger);
        var index = service.BuildIndexDefinition();

        var field = index.Fields.First(f => f.Name == PatternFieldNames.Confidence);
        Assert.True(field.IsFilterable);
        Assert.True(field.IsSortable);
    }

    [Fact]
    public void BuildIndexDefinition_Tags_IsFacetable()
    {
        var service = new AzureSearchPatternIndexingService(null!, _settings, _logger);
        var index = service.BuildIndexDefinition();

        var field = index.Fields.First(f => f.Name == PatternFieldNames.Tags);
        Assert.True(field.IsFacetable);
        Assert.True(field.IsFilterable);
    }

    [Fact]
    public void ToSearchDocument_MapsAllFields()
    {
        var pattern = CreateTestPattern();
        var doc = AzureSearchPatternIndexingService.ToSearchDocument(pattern);

        Assert.Equal(pattern.PatternId, doc[PatternFieldNames.PatternId]);
        Assert.Equal(pattern.Title, doc[PatternFieldNames.Title]);
        Assert.Equal(pattern.ProblemStatement, doc[PatternFieldNames.ProblemStatement]);
        Assert.Equal(pattern.RootCause, doc[PatternFieldNames.RootCause]);
        Assert.Equal(string.Join("\n", pattern.Symptoms), doc[PatternFieldNames.Symptoms]);
        Assert.Equal(string.Join("\n", pattern.ResolutionSteps), doc[PatternFieldNames.ResolutionSteps]);
        Assert.Equal(pattern.EmbeddingVector, doc[PatternFieldNames.EmbeddingVector]);
        Assert.Equal(pattern.TenantId, doc[PatternFieldNames.TenantId]);
        Assert.Equal("Approved", doc[PatternFieldNames.TrustLevel]);
        Assert.Equal(pattern.ProductArea, doc[PatternFieldNames.ProductArea]);
        Assert.Equal(pattern.UpdatedAt, doc[PatternFieldNames.UpdatedAt]);
        Assert.Equal((double)pattern.Confidence, doc[PatternFieldNames.Confidence]);
        Assert.Equal(pattern.Version, doc[PatternFieldNames.Version]);
        Assert.Equal("Internal", doc[PatternFieldNames.Visibility]);
        Assert.Equal(pattern.AccessLabel, doc[PatternFieldNames.AccessLabel]);
        Assert.Equal(pattern.SourceUrl, doc[PatternFieldNames.SourceUrl]);
    }

    [Fact]
    public void ToSearchDocument_MapsTagsAsCollection()
    {
        var pattern = CreateTestPattern() with { Tags = ["auth", "sso", "login"] };
        var doc = AzureSearchPatternIndexingService.ToSearchDocument(pattern);

        var tags = doc[PatternFieldNames.Tags] as List<string>;
        Assert.NotNull(tags);
        Assert.Equal(3, tags.Count);
        Assert.Contains("auth", tags);
        Assert.Contains("sso", tags);
        Assert.Contains("login", tags);
    }

    [Fact]
    public void ToSearchDocument_MapsAllowedGroupsAsCollection()
    {
        var pattern = CreateTestPattern() with { AllowedGroups = ["team-alpha", "team-beta"] };
        var doc = AzureSearchPatternIndexingService.ToSearchDocument(pattern);

        var groups = doc[PatternFieldNames.AllowedGroups] as List<string>;
        Assert.NotNull(groups);
        Assert.Equal(2, groups.Count);
        Assert.Contains("team-alpha", groups);
        Assert.Contains("team-beta", groups);
    }

    [Fact]
    public void ToSearchDocument_HandlesNullRootCause()
    {
        var pattern = CreateTestPattern() with { RootCause = null };
        var doc = AzureSearchPatternIndexingService.ToSearchDocument(pattern);

        Assert.Equal(string.Empty, doc[PatternFieldNames.RootCause]);
    }

    [Fact]
    public void ToSearchDocument_HandlesNullProductArea()
    {
        var pattern = CreateTestPattern() with { ProductArea = null };
        var doc = AzureSearchPatternIndexingService.ToSearchDocument(pattern);

        Assert.Equal(string.Empty, doc[PatternFieldNames.ProductArea]);
    }

    [Fact]
    public void ToSearchDocument_HandlesEmptySymptoms()
    {
        var pattern = CreateTestPattern() with { Symptoms = [] };
        var doc = AzureSearchPatternIndexingService.ToSearchDocument(pattern);

        Assert.Equal(string.Empty, doc[PatternFieldNames.Symptoms]);
    }

    [Fact]
    public void ToSearchDocument_HandlesEmptyResolutionSteps()
    {
        var pattern = CreateTestPattern() with { ResolutionSteps = [] };
        var doc = AzureSearchPatternIndexingService.ToSearchDocument(pattern);

        Assert.Equal(string.Empty, doc[PatternFieldNames.ResolutionSteps]);
    }

    [Fact]
    public void ToSearchDocument_CastsConfidenceToDouble()
    {
        var pattern = CreateTestPattern() with { Confidence = 0.85f };
        var doc = AzureSearchPatternIndexingService.ToSearchDocument(pattern);

        Assert.IsType<double>(doc[PatternFieldNames.Confidence]);
        Assert.Equal(0.85, (double)doc[PatternFieldNames.Confidence], precision: 2);
    }

    [Fact]
    public void ToSearchDocument_MapsTrustLevelAsString()
    {
        foreach (var level in new[] { TrustLevel.Draft, TrustLevel.Reviewed, TrustLevel.Approved, TrustLevel.Deprecated })
        {
            var pattern = CreateTestPattern() with { TrustLevel = level };
            var doc = AzureSearchPatternIndexingService.ToSearchDocument(pattern);

            Assert.Equal(level.ToString(), doc[PatternFieldNames.TrustLevel]);
        }
    }

    [Fact]
    public async Task IndexPatternsAsync_ReturnsEmpty_WhenNoPatterns()
    {
        var service = new AzureSearchPatternIndexingService(null!, _settings, _logger);
        var result = await service.IndexPatternsAsync([]);

        Assert.Equal(0, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.Empty(result.FailedChunkIds);
    }

    [Fact]
    public async Task DeletePatternsAsync_ReturnsZero_WhenNoIds()
    {
        var service = new AzureSearchPatternIndexingService(null!, _settings, _logger);
        var result = await service.DeletePatternsAsync([]);

        Assert.Equal(0, result);
    }

    private static CasePattern CreateTestPattern() => new()
    {
        PatternId = "pattern-abc123",
        TenantId = "tenant-1",
        Title = "SSO Login Timeout Resolution",
        ProblemStatement = "Users experience timeout errors during SSO login when IdP response is delayed.",
        RootCause = "Token validation timeout set too low for federated IdP chains.",
        Symptoms = ["Login page shows 'Request Timed Out'", "Only affects SSO users, not local auth"],
        ResolutionSteps = ["Increase token validation timeout to 30s", "Add retry logic for IdP token exchange"],
        Tags = ["sso", "auth", "timeout"],
        EmbeddingVector = Enumerable.Range(0, PatternFieldNames.EmbeddingDimensions).Select(i => (float)i / 1536f).ToArray(),
        TrustLevel = TrustLevel.Approved,
        Confidence = 0.92f,
        Version = 2,
        ProductArea = "Authentication",
        Visibility = AccessVisibility.Internal,
        AllowedGroups = [],
        AccessLabel = "Internal",
        SourceUrl = "https://smartkb.example.com/patterns/abc123",
        RelatedEvidenceIds = ["ev-001", "ev-002"],
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-7),
        UpdatedAt = DateTimeOffset.UtcNow,
    };
}
