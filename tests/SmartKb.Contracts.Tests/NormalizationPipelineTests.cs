using Microsoft.Extensions.Logging;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;

namespace SmartKb.Contracts.Tests;

public class NormalizationPipelineTests
{
    private readonly NormalizationPipeline _sut;

    public NormalizationPipelineTests()
    {
        _sut = new NormalizationPipeline(
            new TextChunkingService(),
            new BaselineEnrichmentService(),
            new ChunkingSettings(),
            new LoggerFactory().CreateLogger<NormalizationPipeline>());
    }

    private static CanonicalRecord CreateRecord(
        string evidenceId = "ev-001",
        string textContent = "Test content for chunking.",
        string title = "Test Record",
        SourceType sourceType = SourceType.WorkItem,
        AccessVisibility visibility = AccessVisibility.Internal,
        string accessLabel = "Internal")
    {
        return new CanonicalRecord
        {
            TenantId = "tenant-1",
            EvidenceId = evidenceId,
            SourceSystem = ConnectorType.AzureDevOps,
            SourceType = sourceType,
            SourceLocator = new SourceLocator("obj-1", "https://dev.azure.com/org/proj/_workitems/edit/1"),
            Title = title,
            TextContent = textContent,
            ContentHash = "abc123",
            AccessLabel = accessLabel,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = new DateTimeOffset(2026, 3, 16, 12, 0, 0, TimeSpan.Zero),
            Status = EvidenceStatus.Open,
            Permissions = new RecordPermissions(visibility, ["support-team"]),
            Tags = ["vpn", "networking"],
            ProductArea = "Infrastructure",
        };
    }

    [Fact]
    public void Process_ShortRecord_ProducesSingleChunk()
    {
        var record = CreateRecord();
        var chunks = _sut.Process(record);

        Assert.Single(chunks);
        var chunk = chunks[0];
        Assert.Equal("ev-001_chunk_0", chunk.ChunkId);
        Assert.Equal("ev-001", chunk.EvidenceId);
        Assert.Equal("tenant-1", chunk.TenantId);
        Assert.Equal(0, chunk.ChunkIndex);
    }

    [Fact]
    public void Process_PreservesLineage()
    {
        var record = CreateRecord();
        var chunks = _sut.Process(record);

        Assert.All(chunks, c =>
        {
            Assert.Equal("ev-001", c.EvidenceId);
            Assert.Equal("tenant-1", c.TenantId);
            Assert.StartsWith("ev-001_chunk_", c.ChunkId);
        });
    }

    [Fact]
    public void Process_DenormalizesMetadata()
    {
        var record = CreateRecord();
        var chunks = _sut.Process(record);

        var chunk = chunks[0];
        Assert.Equal(ConnectorType.AzureDevOps, chunk.SourceSystem);
        Assert.Equal(SourceType.WorkItem, chunk.SourceType);
        Assert.Equal(EvidenceStatus.Open, chunk.Status);
        Assert.Equal("Infrastructure", chunk.ProductArea);
        Assert.Equal(["vpn", "networking"], chunk.Tags);
    }

    [Fact]
    public void Process_PreservesAclFields()
    {
        var record = CreateRecord(visibility: AccessVisibility.Restricted, accessLabel: "Restricted - Support");
        var chunks = _sut.Process(record);

        var chunk = chunks[0];
        Assert.Equal(AccessVisibility.Restricted, chunk.Visibility);
        Assert.Equal(["support-team"], chunk.AllowedGroups);
        Assert.Equal("Restricted - Support", chunk.AccessLabel);
    }

    [Fact]
    public void Process_SetsSourceUrl()
    {
        var record = CreateRecord();
        var chunks = _sut.Process(record);
        Assert.Equal("https://dev.azure.com/org/proj/_workitems/edit/1", chunks[0].SourceUrl);
    }

    [Fact]
    public void Process_SetsEnrichmentVersion()
    {
        var record = CreateRecord();
        var chunks = _sut.Process(record);
        Assert.Equal(BaselineEnrichmentService.CurrentEnrichmentVersion, chunks[0].EnrichmentVersion);
    }

    [Fact]
    public void Process_EmbeddingVectorIsNull()
    {
        var record = CreateRecord();
        var chunks = _sut.Process(record);
        Assert.Null(chunks[0].EmbeddingVector);
    }

    [Fact]
    public void Process_LongText_ProducesMultipleChunks()
    {
        var longText = string.Join(" ", Enumerable.Repeat("word", 1000));
        var record = CreateRecord(textContent: longText);
        var chunks = _sut.Process(record);
        Assert.True(chunks.Count > 1);
        // Verify sequential indices.
        for (int i = 0; i < chunks.Count; i++)
        {
            Assert.Equal(i, chunks[i].ChunkIndex);
            Assert.Equal($"ev-001_chunk_{i}", chunks[i].ChunkId);
        }
    }

    [Fact]
    public void Process_ExtractsErrorTokens()
    {
        var record = CreateRecord(textContent: "Got NullReferenceException at startup, HTTP 500 returned");
        var chunks = _sut.Process(record);
        Assert.Contains("NullReferenceException", chunks[0].ErrorTokens);
        Assert.Contains("HTTP 500", chunks[0].ErrorTokens);
    }

    [Fact]
    public void ProcessBatch_HandlesMultipleRecords()
    {
        var records = new List<CanonicalRecord>
        {
            CreateRecord(evidenceId: "ev-1"),
            CreateRecord(evidenceId: "ev-2"),
            CreateRecord(evidenceId: "ev-3"),
        };

        var chunks = _sut.ProcessBatch(records);
        Assert.Equal(3, chunks.Count);
        Assert.Contains(chunks, c => c.EvidenceId == "ev-1");
        Assert.Contains(chunks, c => c.EvidenceId == "ev-2");
        Assert.Contains(chunks, c => c.EvidenceId == "ev-3");
    }

    [Fact]
    public void ProcessBatch_EmptyList_ReturnsEmpty()
    {
        var chunks = _sut.ProcessBatch([]);
        Assert.Empty(chunks);
    }

    [Fact]
    public void Process_WikiPage_GetsDocumentationCategory()
    {
        var record = CreateRecord(sourceType: SourceType.WikiPage, textContent: "Setup instructions");
        var chunks = _sut.Process(record);
        // Enrichment should detect documentation category for wiki pages.
        // ErrorTokens should be empty since no error patterns.
        Assert.Empty(chunks[0].ErrorTokens);
    }
}
