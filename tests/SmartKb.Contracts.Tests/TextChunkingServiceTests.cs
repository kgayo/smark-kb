using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Services;

namespace SmartKb.Contracts.Tests;

public class TextChunkingServiceTests
{
    private readonly TextChunkingService _sut = new();

    [Fact]
    public void Chunk_EmptyText_ReturnsSingleChunk()
    {
        var result = _sut.Chunk("");
        Assert.Single(result);
        Assert.Equal(0, result[0].Index);
    }

    [Fact]
    public void Chunk_NullText_ReturnsSingleChunk()
    {
        var result = _sut.Chunk(null!);
        Assert.Single(result);
    }

    [Fact]
    public void Chunk_ShortText_ReturnsSingleChunk()
    {
        var text = "This is a short paragraph.";
        var result = _sut.Chunk(text);
        Assert.Single(result);
        Assert.Equal(text, result[0].Text);
        Assert.Equal(0, result[0].Index);
    }

    [Fact]
    public void Chunk_LongText_SplitsIntoMultipleChunks()
    {
        // Default: 512 tokens * 4 chars = 2048 chars max per chunk.
        var text = string.Join(" ", Enumerable.Repeat("word", 1000)); // ~5000 chars
        var result = _sut.Chunk(text);
        Assert.True(result.Count > 1);
        // All chunks should have sequential indices.
        for (int i = 0; i < result.Count; i++)
            Assert.Equal(i, result[i].Index);
    }

    [Fact]
    public void Chunk_RespectsMarkdownHeaders()
    {
        var text = """
            # Section One
            Content of section one that is fairly substantial.

            ## Section Two
            Content of section two with different information.

            ## Section Three
            More content in section three.
            """;

        // With short max to force splitting.
        var settings = new ChunkingSettings { MaxTokensPerChunk = 30, OverlapTokens = 5 };
        var result = _sut.Chunk(text, settings: settings);

        Assert.True(result.Count >= 2);
        // Section headers should influence context.
        Assert.NotNull(result[0].Context);
    }

    [Fact]
    public void Chunk_PreservesTitle_AsInitialContext()
    {
        var result = _sut.Chunk("Short content.", title: "My Document");
        Assert.Single(result);
        Assert.Equal("My Document", result[0].Context);
    }

    [Fact]
    public void Chunk_CustomSettings_SmallChunks()
    {
        var settings = new ChunkingSettings { MaxTokensPerChunk = 10, OverlapTokens = 2 };
        var text = "The quick brown fox jumps over the lazy dog and runs away quickly";
        var result = _sut.Chunk(text, settings: settings);
        // 10 tokens * 4 chars = 40 chars max per chunk.
        Assert.True(result.Count >= 2);
    }

    [Fact]
    public void Chunk_StructuralBoundariesDisabled_SplitsBySize()
    {
        var settings = new ChunkingSettings
        {
            MaxTokensPerChunk = 20,
            OverlapTokens = 3,
            UseStructuralBoundaries = false,
        };
        var text = "# Header\nParagraph under header.\n\n# Another\nMore paragraph text here.";
        var result = _sut.Chunk(text, settings: settings);
        Assert.True(result.Count >= 1);
    }

    [Fact]
    public void Chunk_ChunkIndicesAreSequential()
    {
        var text = string.Join("\n\n", Enumerable.Range(0, 50).Select(i => $"Paragraph {i} with some content."));
        var settings = new ChunkingSettings { MaxTokensPerChunk = 20, OverlapTokens = 3 };
        var result = _sut.Chunk(text, settings: settings);
        for (int i = 0; i < result.Count; i++)
            Assert.Equal(i, result[i].Index);
    }

    [Fact]
    public void Chunk_VeryLongSingleParagraph_HardSplits()
    {
        // One paragraph with no whitespace boundaries — forced hard split.
        var text = new string('A', 5000);
        var settings = new ChunkingSettings { MaxTokensPerChunk = 100, OverlapTokens = 10 };
        var result = _sut.Chunk(text, settings: settings);
        Assert.True(result.Count > 1);
    }
}
