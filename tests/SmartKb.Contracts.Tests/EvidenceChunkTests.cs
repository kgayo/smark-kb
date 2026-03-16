using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Search;

namespace SmartKb.Contracts.Tests;

public class EvidenceChunkTests
{
    private static EvidenceChunk CreateSample(int chunkIndex = 0) => new()
    {
        ChunkId = $"ev-001_chunk_{chunkIndex}",
        EvidenceId = "ev-001",
        TenantId = "tenant-1",
        ChunkIndex = chunkIndex,
        ChunkText = "How to configure VPN for remote access...",
        ChunkContext = "Setup Guide > Networking > VPN",
        SourceSystem = ConnectorType.AzureDevOps,
        SourceType = SourceType.WikiPage,
        Status = EvidenceStatus.Open,
        UpdatedAt = DateTimeOffset.UtcNow,
        Visibility = AccessVisibility.Internal,
        AllowedGroups = ["support-team"],
        AccessLabel = "Internal - Support Team",
        Title = "Setup Guide",
        SourceUrl = "https://dev.azure.com/org/proj/_wiki/page-1"
    };

    [Fact]
    public void EvidenceChunk_RequiredFieldsPopulated()
    {
        var chunk = CreateSample();

        Assert.Equal("ev-001_chunk_0", chunk.ChunkId);
        Assert.Equal("ev-001", chunk.EvidenceId);
        Assert.Equal("tenant-1", chunk.TenantId);
        Assert.Equal(0, chunk.ChunkIndex);
        Assert.Contains("VPN", chunk.ChunkText);
        Assert.Equal(AccessVisibility.Internal, chunk.Visibility);
        Assert.Single(chunk.AllowedGroups);
    }

    [Fact]
    public void EvidenceChunk_EmbeddingVectorNullByDefault()
    {
        var chunk = CreateSample();
        Assert.Null(chunk.EmbeddingVector);
    }

    [Fact]
    public void EvidenceChunk_EmbeddingVectorCorrectDimensions()
    {
        var vector = new float[SearchFieldNames.EmbeddingDimensions];
        var chunk = CreateSample() with { EmbeddingVector = vector };

        Assert.NotNull(chunk.EmbeddingVector);
        Assert.Equal(1536, chunk.EmbeddingVector!.Length);
    }

    [Fact]
    public void EvidenceChunk_TagsDefaultEmpty()
    {
        var chunk = CreateSample();
        Assert.Empty(chunk.Tags);
    }

    [Fact]
    public void EvidenceChunk_ChunkIdNamingConvention()
    {
        var chunk0 = CreateSample(0);
        var chunk3 = CreateSample(3);

        Assert.Equal("ev-001_chunk_0", chunk0.ChunkId);
        Assert.Equal("ev-001_chunk_3", chunk3.ChunkId);
    }
}
