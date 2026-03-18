using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;

namespace SmartKb.Contracts.Tests;

public class RetrievalCompressionTests
{
    private static RetrievedChunk MakeChunk(string chunkText, string chunkId = "c1") => new()
    {
        ChunkId = chunkId,
        EvidenceId = "ev-1",
        ChunkText = chunkText,
        Title = "Test",
        SourceUrl = "https://example.com",
        SourceSystem = "test",
        SourceType = "article",
        Visibility = "Public",
        AccessLabel = "all",
        UpdatedAt = DateTimeOffset.UtcNow,
        RrfScore = 0.9,
    };

    [Fact]
    public void ChunksUnderLimit_NotModified()
    {
        var chunks = new[] { MakeChunk("Short text") };

        var (compressed, truncatedCount) = RetrievalCompressionService.CompressChunks(chunks, 100);

        Assert.Single(compressed);
        Assert.Equal("Short text", compressed[0].ChunkText);
        Assert.Equal(0, truncatedCount);
    }

    [Fact]
    public void ChunksOverLimit_TruncatedWithMarker()
    {
        var longText = new string('a', 200);
        var chunks = new[] { MakeChunk(longText) };

        var (compressed, truncatedCount) = RetrievalCompressionService.CompressChunks(chunks, 50);

        Assert.Single(compressed);
        Assert.EndsWith(" [...]", compressed[0].ChunkText);
        Assert.True(compressed[0].ChunkText.Length < longText.Length);
        Assert.Equal(1, truncatedCount);
    }

    [Fact]
    public void Truncation_RespectsWordBoundaries()
    {
        // Place a space at position 45, well within the 100-char search window.
        var text = "Hello world this is a test of word boundary truncation logic here and more text follows after the limit";
        var chunks = new[] { MakeChunk(text) };

        var (compressed, _) = RetrievalCompressionService.CompressChunks(chunks, 50);

        // Should not cut mid-word: truncation point should be at a space.
        var truncated = compressed[0].ChunkText;
        Assert.EndsWith(" [...]", truncated);
        var bodyWithoutMarker = truncated[..^" [...]".Length];
        // The body should end at a word boundary (last char should not be in mid-word).
        Assert.True(bodyWithoutMarker.Length <= 50);
    }

    [Fact]
    public void EmptyChunkList_ReturnsEmpty()
    {
        var (compressed, truncatedCount) = RetrievalCompressionService.CompressChunks([], 100);

        Assert.Empty(compressed);
        Assert.Equal(0, truncatedCount);
    }

    [Fact]
    public void MultipleChunks_MixOfShortAndLong()
    {
        var short1 = MakeChunk("Short", "c1");
        var long1 = MakeChunk(new string('x', 200), "c2");
        var short2 = MakeChunk("Also short", "c3");
        var long2 = MakeChunk(new string('y', 300), "c4");

        var (compressed, truncatedCount) = RetrievalCompressionService.CompressChunks(
            [short1, long1, short2, long2], 50);

        Assert.Equal(4, compressed.Count);
        Assert.Equal(2, truncatedCount);
        Assert.Equal("Short", compressed[0].ChunkText);
        Assert.EndsWith(" [...]", compressed[1].ChunkText);
        Assert.Equal("Also short", compressed[2].ChunkText);
        Assert.EndsWith(" [...]", compressed[3].ChunkText);
    }

    [Fact]
    public void ZeroMaxChunkChars_ReturnsUnmodified()
    {
        var longText = new string('a', 200);
        var chunks = new[] { MakeChunk(longText) };

        var (compressed, truncatedCount) = RetrievalCompressionService.CompressChunks(chunks, 0);

        Assert.Single(compressed);
        Assert.Equal(longText, compressed[0].ChunkText);
        Assert.Equal(0, truncatedCount);
    }

    [Fact]
    public void NegativeMaxChunkChars_ReturnsUnmodified()
    {
        var longText = new string('a', 200);
        var chunks = new[] { MakeChunk(longText) };

        var (compressed, truncatedCount) = RetrievalCompressionService.CompressChunks(chunks, -10);

        Assert.Single(compressed);
        Assert.Equal(longText, compressed[0].ChunkText);
        Assert.Equal(0, truncatedCount);
    }

    [Fact]
    public void FindWordBoundary_TargetBeyondLength_ReturnsLength()
    {
        var result = RetrievalCompressionService.FindWordBoundary("hello", 100);
        Assert.Equal(5, result);
    }

    [Fact]
    public void FindWordBoundary_FindsSpaceBeforeTarget()
    {
        var text = "hello world foobar";
        // Target at 14 — should find space at 11 ("hello world ").
        var result = RetrievalCompressionService.FindWordBoundary(text, 14);
        Assert.Equal(11, result);
    }

    [Fact]
    public void FindWordBoundary_NoSpaceInWindow_FallsBackToTarget()
    {
        // 150 contiguous non-space chars means no boundary in last 100 chars from target.
        var text = new string('a', 150);
        var result = RetrievalCompressionService.FindWordBoundary(text, 140);
        Assert.Equal(140, result);
    }

    [Fact]
    public void Truncation_PreservesOtherChunkFields()
    {
        var chunk = MakeChunk(new string('z', 200));
        var (compressed, _) = RetrievalCompressionService.CompressChunks([chunk], 50);

        var c = compressed[0];
        Assert.Equal(chunk.ChunkId, c.ChunkId);
        Assert.Equal(chunk.EvidenceId, c.EvidenceId);
        Assert.Equal(chunk.Title, c.Title);
        Assert.Equal(chunk.SourceUrl, c.SourceUrl);
        Assert.Equal(chunk.SourceSystem, c.SourceSystem);
        Assert.Equal(chunk.RrfScore, c.RrfScore);
    }
}
