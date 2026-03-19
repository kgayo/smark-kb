using Azure.Search.Documents.Indexes.Models;
using Microsoft.Extensions.Logging.Abstractions;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Search;
using SmartKb.Contracts.Services;
using SmartKb.Data.Entities;
using SmartKb.Data.Repositories;

namespace SmartKb.Data.Tests;

public class IndexMigrationServiceTests : IDisposable
{
    private readonly SmartKbDbContext _db;

    public IndexMigrationServiceTests()
    {
        _db = TestDbContextFactory.Create();
    }

    public void Dispose() => _db.Dispose();

    // --- ComputeSchemaHash ---

    [Fact]
    public void ComputeSchemaHash_SameIndex_ReturnsDeterministicHash()
    {
        var index = BuildTestIndex("test-index");
        var hash1 = IndexMigrationService.ComputeSchemaHash(index);
        var hash2 = IndexMigrationService.ComputeSchemaHash(index);

        Assert.NotEmpty(hash1);
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ComputeSchemaHash_DifferentFieldOrder_ReturnsSameHash()
    {
        var index1 = new SearchIndex("test")
        {
            Fields =
            {
                new SimpleField("field_a", SearchFieldDataType.String) { IsFilterable = true },
                new SimpleField("field_b", SearchFieldDataType.String) { IsKey = true },
            },
        };

        var index2 = new SearchIndex("test")
        {
            Fields =
            {
                new SimpleField("field_b", SearchFieldDataType.String) { IsKey = true },
                new SimpleField("field_a", SearchFieldDataType.String) { IsFilterable = true },
            },
        };

        Assert.Equal(
            IndexMigrationService.ComputeSchemaHash(index1),
            IndexMigrationService.ComputeSchemaHash(index2));
    }

    [Fact]
    public void ComputeSchemaHash_AddedField_ReturnsDifferentHash()
    {
        var index1 = BuildTestIndex("test");
        var index2 = BuildTestIndex("test");
        index2.Fields.Add(new SimpleField("new_field", SearchFieldDataType.String) { IsFilterable = true });

        Assert.NotEqual(
            IndexMigrationService.ComputeSchemaHash(index1),
            IndexMigrationService.ComputeSchemaHash(index2));
    }

    [Fact]
    public void ComputeSchemaHash_ChangedFieldType_ReturnsDifferentHash()
    {
        var index1 = new SearchIndex("test")
        {
            Fields = { new SimpleField("f1", SearchFieldDataType.String) { IsFilterable = true } },
        };
        var index2 = new SearchIndex("test")
        {
            Fields = { new SimpleField("f1", SearchFieldDataType.Int32) { IsFilterable = true } },
        };

        Assert.NotEqual(
            IndexMigrationService.ComputeSchemaHash(index1),
            IndexMigrationService.ComputeSchemaHash(index2));
    }

    [Fact]
    public void ComputeSchemaHash_ChangedFieldAttribute_ReturnsDifferentHash()
    {
        var index1 = new SearchIndex("test")
        {
            Fields = { new SimpleField("f1", SearchFieldDataType.String) { IsFilterable = true } },
        };
        var index2 = new SearchIndex("test")
        {
            Fields = { new SimpleField("f1", SearchFieldDataType.String) { IsFilterable = false } },
        };

        Assert.NotEqual(
            IndexMigrationService.ComputeSchemaHash(index1),
            IndexMigrationService.ComputeSchemaHash(index2));
    }

    [Fact]
    public void ComputeSchemaHash_EvidenceIndex_ProducesNonEmptyHash()
    {
        var settings = new SearchServiceSettings { EvidenceIndexName = "evidence" };
        var service = new AzureSearchIndexingService(null!, settings, NullLogger<AzureSearchIndexingService>.Instance);
        var index = service.BuildIndexDefinition();
        var hash = IndexMigrationService.ComputeSchemaHash(index);

        Assert.NotEmpty(hash);
        Assert.Equal(64, hash.Length); // SHA-256 hex = 64 chars
    }

    [Fact]
    public void ComputeSchemaHash_PatternIndex_ProducesNonEmptyHash()
    {
        var settings = new SearchServiceSettings { PatternIndexName = "patterns" };
        var service = new AzureSearchPatternIndexingService(null!, settings, NullLogger<AzureSearchPatternIndexingService>.Instance);
        var index = service.BuildIndexDefinition();
        var hash = IndexMigrationService.ComputeSchemaHash(index);

        Assert.NotEmpty(hash);
        Assert.Equal(64, hash.Length);
    }

    // --- Version tracking DB operations ---

    [Fact]
    public async Task GetCurrentVersionAsync_NoVersions_ReturnsNull()
    {
        var service = CreateServiceWithoutSearch();
        var result = await service.GetCurrentVersionAsync(IndexType.Evidence);
        Assert.Null(result);
    }

    [Fact]
    public async Task EnsureVersionTrackingAsync_NoExistingVersion_CreatesV1()
    {
        var service = CreateServiceWithoutSearch();
        var result = await service.EnsureVersionTrackingAsync(IndexType.Evidence, "admin");

        Assert.NotNull(result);
        Assert.Equal(1, result.Version);
        Assert.Equal(IndexVersionStatus.Active, result.Status);
        Assert.Equal(IndexType.Evidence, result.IndexType);
        Assert.Equal("evidence", result.IndexName);
        Assert.Equal("admin", result.CreatedBy);
        Assert.NotNull(result.ActivatedAt);
    }

    [Fact]
    public async Task EnsureVersionTrackingAsync_ExistingVersion_ReturnsExisting()
    {
        var service = CreateServiceWithoutSearch();
        var first = await service.EnsureVersionTrackingAsync(IndexType.Evidence, "admin");
        var second = await service.EnsureVersionTrackingAsync(IndexType.Evidence, "admin");

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.Version, second.Version);
    }

    [Fact]
    public async Task ListVersionsAsync_ReturnsAllVersionsDescending()
    {
        // Seed two versions manually.
        _db.IndexSchemaVersions.AddRange(
            new IndexSchemaVersionEntity
            {
                Id = Guid.NewGuid(), IndexType = IndexType.Evidence,
                IndexName = "evidence-v1", Version = 1, SchemaHash = "hash1",
                Status = IndexVersionStatus.Retired, CreatedBy = "sys",
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-2),
                RetiredAt = DateTimeOffset.UtcNow.AddHours(-1),
            },
            new IndexSchemaVersionEntity
            {
                Id = Guid.NewGuid(), IndexType = IndexType.Evidence,
                IndexName = "evidence-v2", Version = 2, SchemaHash = "hash2",
                Status = IndexVersionStatus.Active, CreatedBy = "sys",
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
                ActivatedAt = DateTimeOffset.UtcNow,
            });
        await _db.SaveChangesAsync();

        var service = CreateServiceWithoutSearch();
        var versions = await service.ListVersionsAsync(IndexType.Evidence);

        Assert.Equal(2, versions.Count);
        Assert.Equal(2, versions[0].Version);
        Assert.Equal(1, versions[1].Version);
    }

    [Fact]
    public async Task GetCurrentVersionAsync_ReturnsActiveVersion()
    {
        _db.IndexSchemaVersions.Add(new IndexSchemaVersionEntity
        {
            Id = Guid.NewGuid(), IndexType = IndexType.Evidence,
            IndexName = "evidence-v1", Version = 1, SchemaHash = "hash1",
            Status = IndexVersionStatus.Active, CreatedBy = "sys",
            CreatedAt = DateTimeOffset.UtcNow,
            ActivatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        var service = CreateServiceWithoutSearch();
        var result = await service.GetCurrentVersionAsync(IndexType.Evidence);

        Assert.NotNull(result);
        Assert.Equal("evidence-v1", result!.IndexName);
        Assert.Equal(IndexVersionStatus.Active, result.Status);
    }

    [Fact]
    public async Task PlanMigrationAsync_NoCurrentVersion_PlansMigration()
    {
        var service = CreateServiceWithoutSearch();
        var plan = await service.PlanMigrationAsync(IndexType.Evidence);

        Assert.True(plan.MigrationNeeded);
        Assert.Equal(0, plan.CurrentVersion);
        Assert.Equal(1, plan.NewVersion);
        Assert.Equal("evidence-v1", plan.NewIndexName);
        Assert.NotEmpty(plan.DesiredSchemaHash);
    }

    [Fact]
    public async Task PlanMigrationAsync_SchemaUnchanged_NoMigration()
    {
        // Bootstrap v1 with current schema hash.
        var service = CreateServiceWithoutSearch();
        await service.EnsureVersionTrackingAsync(IndexType.Evidence, "admin");

        var plan = await service.PlanMigrationAsync(IndexType.Evidence);

        Assert.False(plan.MigrationNeeded);
        Assert.Equal(1, plan.CurrentVersion);
        Assert.Equal(2, plan.NewVersion);
        Assert.Equal("evidence-v2", plan.NewIndexName);
    }

    [Fact]
    public async Task PlanMigrationAsync_SchemaChanged_MigrationNeeded()
    {
        // Bootstrap with a stale hash.
        _db.IndexSchemaVersions.Add(new IndexSchemaVersionEntity
        {
            Id = Guid.NewGuid(), IndexType = IndexType.Evidence,
            IndexName = "evidence-v1", Version = 1, SchemaHash = "stale-hash",
            Status = IndexVersionStatus.Active, CreatedBy = "sys",
            CreatedAt = DateTimeOffset.UtcNow,
            ActivatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        var service = CreateServiceWithoutSearch();
        var plan = await service.PlanMigrationAsync(IndexType.Evidence);

        Assert.True(plan.MigrationNeeded);
        Assert.Equal(1, plan.CurrentVersion);
        Assert.Equal(2, plan.NewVersion);
        Assert.Equal("evidence-v2", plan.NewIndexName);
        Assert.NotEqual("stale-hash", plan.DesiredSchemaHash);
    }

    [Fact]
    public async Task DeleteRetiredVersionAsync_NonRetiredVersion_ReturnsFalse()
    {
        var id = Guid.NewGuid();
        _db.IndexSchemaVersions.Add(new IndexSchemaVersionEntity
        {
            Id = id, IndexType = IndexType.Evidence,
            IndexName = "evidence-v1", Version = 1, SchemaHash = "hash1",
            Status = IndexVersionStatus.Active, CreatedBy = "sys",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        var service = CreateServiceWithoutSearch();
        var result = await service.DeleteRetiredVersionAsync(id, "admin");

        Assert.False(result);
    }

    [Fact]
    public async Task DeleteRetiredVersionAsync_UnknownId_ReturnsFalse()
    {
        var service = CreateServiceWithoutSearch();
        var result = await service.DeleteRetiredVersionAsync(Guid.NewGuid(), "admin");
        Assert.False(result);
    }

    [Fact]
    public async Task RollbackAsync_NoActiveVersion_Fails()
    {
        var service = CreateServiceWithoutSearch();
        var result = await service.RollbackAsync(IndexType.Evidence, "admin");

        Assert.False(result.Success);
        Assert.Contains("No active version", result.Error);
    }

    [Fact]
    public async Task RollbackAsync_NoRetiredVersion_Fails()
    {
        _db.IndexSchemaVersions.Add(new IndexSchemaVersionEntity
        {
            Id = Guid.NewGuid(), IndexType = IndexType.Evidence,
            IndexName = "evidence-v1", Version = 1, SchemaHash = "hash1",
            Status = IndexVersionStatus.Active, CreatedBy = "sys",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        var service = CreateServiceWithoutSearch();
        var result = await service.RollbackAsync(IndexType.Evidence, "admin");

        Assert.False(result.Success);
        Assert.Contains("No retired version", result.Error);
    }

    [Fact]
    public async Task ListVersionsAsync_FiltersByIndexType()
    {
        _db.IndexSchemaVersions.AddRange(
            new IndexSchemaVersionEntity
            {
                Id = Guid.NewGuid(), IndexType = IndexType.Evidence,
                IndexName = "evidence-v1", Version = 1, SchemaHash = "h1",
                Status = IndexVersionStatus.Active, CreatedBy = "sys",
                CreatedAt = DateTimeOffset.UtcNow,
            },
            new IndexSchemaVersionEntity
            {
                Id = Guid.NewGuid(), IndexType = IndexType.Patterns,
                IndexName = "patterns-v1", Version = 1, SchemaHash = "h2",
                Status = IndexVersionStatus.Active, CreatedBy = "sys",
                CreatedAt = DateTimeOffset.UtcNow,
            });
        await _db.SaveChangesAsync();

        var service = CreateServiceWithoutSearch();

        var evidenceVersions = await service.ListVersionsAsync(IndexType.Evidence);
        var patternVersions = await service.ListVersionsAsync(IndexType.Patterns);

        Assert.Single(evidenceVersions);
        Assert.Single(patternVersions);
        Assert.Equal("evidence-v1", evidenceVersions[0].IndexName);
        Assert.Equal("patterns-v1", patternVersions[0].IndexName);
    }

    // --- IndexType and IndexVersionStatus constants ---

    [Fact]
    public void IndexType_Constants_AreCorrect()
    {
        Assert.Equal("evidence", IndexType.Evidence);
        Assert.Equal("patterns", IndexType.Patterns);
    }

    [Fact]
    public void IndexVersionStatus_Constants_AreCorrect()
    {
        Assert.Equal("Active", IndexVersionStatus.Active);
        Assert.Equal("Migrating", IndexVersionStatus.Migrating);
        Assert.Equal("Retired", IndexVersionStatus.Retired);
    }

    // --- Helper ---

    private IndexMigrationService CreateServiceWithoutSearch()
    {
        var settings = new SearchServiceSettings
        {
            EvidenceIndexName = "evidence",
            PatternIndexName = "patterns",
        };
        var evidenceIndexing = new AzureSearchIndexingService(
            null!, settings, NullLogger<AzureSearchIndexingService>.Instance);
        var patternIndexing = new AzureSearchPatternIndexingService(
            null!, settings, NullLogger<AzureSearchPatternIndexingService>.Instance);

        return new IndexMigrationService(
            _db, null!, settings, evidenceIndexing, patternIndexing,
            NullLogger<IndexMigrationService>.Instance);
    }

    private static SearchIndex BuildTestIndex(string name)
    {
        return new SearchIndex(name)
        {
            Fields =
            {
                new SimpleField("id", SearchFieldDataType.String) { IsKey = true },
                new SearchableField("content") { AnalyzerName = LexicalAnalyzerName.EnMicrosoft },
                new SimpleField("tenant_id", SearchFieldDataType.String) { IsFilterable = true },
            },
            VectorSearch = new VectorSearch
            {
                Profiles = { new VectorSearchProfile("test-profile", "test-hnsw") },
                Algorithms = { new HnswAlgorithmConfiguration("test-hnsw") },
            },
            SemanticSearch = new SemanticSearch
            {
                Configurations =
                {
                    new SemanticConfiguration("test-semantic", new SemanticPrioritizedFields
                    {
                        ContentFields = { new SemanticField("content") },
                    }),
                },
            },
        };
    }
}
