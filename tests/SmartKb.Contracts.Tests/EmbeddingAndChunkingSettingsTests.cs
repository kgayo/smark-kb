using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Search;

namespace SmartKb.Contracts.Tests;

public class EmbeddingAndChunkingSettingsTests
{
    [Fact]
    public void EmbeddingSettings_DefaultsMatchDecisionD001()
    {
        var settings = new EmbeddingSettings();

        Assert.Equal("text-embedding-3-large", settings.ModelId);
        Assert.Equal(1536, settings.Dimensions);
        Assert.Equal(8191, settings.MaxInputTokens);
        Assert.Equal("Embedding", EmbeddingSettings.SectionName);
    }

    [Fact]
    public void ChunkingSettings_DefaultsMatchDecisionD002()
    {
        var settings = new ChunkingSettings();

        Assert.Equal(512, settings.MaxTokensPerChunk);
        Assert.Equal(64, settings.OverlapTokens);
        Assert.True(settings.UseStructuralBoundaries);
        Assert.Equal("Chunking", ChunkingSettings.SectionName);
    }

    [Fact]
    public void ChunkingSettings_OverlapIsReasonablePercentageOfChunkSize()
    {
        var settings = new ChunkingSettings();
        var overlapPercent = (double)settings.OverlapTokens / settings.MaxTokensPerChunk * 100;

        Assert.InRange(overlapPercent, 5, 25);
    }

    [Fact]
    public void EmbeddingSettings_DimensionsMatchSearchFieldConstant()
    {
        var settings = new EmbeddingSettings();
        Assert.Equal(SearchFieldNames.EmbeddingDimensions, settings.Dimensions);
    }
}
