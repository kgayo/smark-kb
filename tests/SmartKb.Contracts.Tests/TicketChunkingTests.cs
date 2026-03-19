using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Services;

namespace SmartKb.Contracts.Tests;

public class TicketChunkingTests
{
    private readonly TextChunkingService _sut = new();

    [Fact]
    public void Chunk_TicketWithSections_SplitsByTroubleshootingStructure()
    {
        var text = """
            Description:
            The application crashes when clicking the submit button on the order form.
            Error code 0x80070005 appears in the event log.

            Steps to Reproduce:
            1. Navigate to the order form.
            2. Fill in all required fields.
            3. Click Submit.
            4. Observe crash.

            Root Cause:
            The submit handler was not checking for null user context after session timeout.
            The null reference propagated to the order validation layer.

            Resolution:
            Added null check for user context in SubmitOrderHandler.
            Added session timeout detection with redirect to login.

            Verification:
            Reproduced the original steps — no crash.
            Verified session timeout redirect works correctly.
            """;

        var settings = new ChunkingSettings { MaxTokensPerChunk = 40, OverlapTokens = 5 };
        var result = _sut.Chunk(text, title: "Order form crash", settings: settings, sourceType: SourceType.Ticket);

        Assert.True(result.Count >= 3, $"Expected at least 3 chunks for structured ticket, got {result.Count}");

        // Verify that section headers appear as context labels.
        var contexts = result.Select(c => c.Context).Where(c => c is not null).ToList();
        Assert.Contains("Steps to Reproduce", contexts);
        Assert.Contains("Resolution", contexts);
    }

    [Fact]
    public void Chunk_WorkItemWithSections_UsesTicketChunking()
    {
        var padding = string.Join(" ", Enumerable.Repeat("Detailed information about the performance issue.", 15));
        var text = $"""
            Problem Description:
            Users report slow page load times on the dashboard. {padding}

            Root Cause:
            N+1 query issue in the metrics aggregation endpoint. {padding}

            Fix Applied:
            Batch query replaces individual lookups. Response time dropped from 4s to 200ms. {padding}
            """;

        var settings = new ChunkingSettings { MaxTokensPerChunk = 60, OverlapTokens = 8 };
        var result = _sut.Chunk(text, settings: settings, sourceType: SourceType.WorkItem);

        Assert.True(result.Count >= 2, $"Expected at least 2 chunks, got {result.Count}");
        var contexts = result.Select(c => c.Context).Where(c => c is not null).ToList();
        Assert.Contains("Root Cause", contexts);
    }

    [Fact]
    public void Chunk_TicketNoSections_FallsBackToGenericChunking()
    {
        // Unstructured ticket text with no recognized section headers.
        var text = string.Join(" ", Enumerable.Repeat("This is unstructured ticket content.", 100));

        var settings = new ChunkingSettings { MaxTokensPerChunk = 50, OverlapTokens = 8 };
        var result = _sut.Chunk(text, title: "Bug report", settings: settings, sourceType: SourceType.Ticket);

        Assert.True(result.Count >= 2, "Long unstructured text should still be split into chunks");
        Assert.Equal(0, result[0].Index);
        // Should fall back to generic chunking without error.
    }

    [Fact]
    public void Chunk_TicketSectionsWithMarkdownHeaders_RecognizesBoth()
    {
        var padding = string.Join(" ", Enumerable.Repeat("Extra context for this section.", 15));
        var text = $"""
            ## Symptoms
            Application returns HTTP 500 on the login endpoint. {padding}

            ## Steps to Reproduce
            1. Send POST /api/auth/login with valid credentials. {padding}

            ## Analysis
            The JWT signing key was rotated but the cache was not invalidated. {padding}

            ## Workaround
            Restart the API service to clear the key cache. {padding}

            ## Resolution
            Added cache invalidation on key rotation events. {padding}
            """;

        var settings = new ChunkingSettings { MaxTokensPerChunk = 60, OverlapTokens = 8 };
        var result = _sut.Chunk(text, settings: settings, sourceType: SourceType.Ticket);

        Assert.True(result.Count >= 3, $"Expected at least 3 chunks, got {result.Count}");
        var contexts = result.Select(c => c.Context).Where(c => c is not null).ToList();
        Assert.Contains("Workaround", contexts);
        Assert.Contains("Resolution", contexts);
    }

    [Fact]
    public void Chunk_TicketSmallEnough_ReturnsSingleChunk()
    {
        var text = """
            Description:
            Minor UI glitch on button hover.

            Resolution:
            CSS fix applied.
            """;

        // Default settings: 512 tokens = 2048 chars — this text fits easily.
        var result = _sut.Chunk(text, sourceType: SourceType.Ticket);

        Assert.Single(result);
        Assert.Equal(0, result[0].Index);
    }

    [Fact]
    public void Chunk_TicketIndicesAreSequential()
    {
        var text = """
            Symptoms:
            Multiple errors in production logs.

            Steps to Reproduce:
            Deploy version 3.2.1 and trigger the batch job.

            Root Cause:
            Database connection pool exhaustion under load.

            Resolution:
            Increased pool size from 10 to 50 and added connection timeout.

            Verification:
            Load test with 500 concurrent users — no connection errors.

            Impact:
            Affected 200 users during the 2-hour window before hotfix.

            Environment:
            Production, Azure SQL, App Service P2v3.
            """;

        var settings = new ChunkingSettings { MaxTokensPerChunk = 50, OverlapTokens = 8 };
        var result = _sut.Chunk(text, settings: settings, sourceType: SourceType.Ticket);

        for (int i = 0; i < result.Count; i++)
            Assert.Equal(i, result[i].Index);
    }

    [Fact]
    public void Chunk_DocumentSourceType_UsesGenericChunking()
    {
        // Document source type should NOT trigger ticket chunking, even with section-like headers.
        var text = """
            Description:
            This is a wiki page about our deployment process.

            Steps to Reproduce:
            This section name collides with ticket patterns but should be treated as a markdown section.

            Resolution:
            Wiki pages should not be chunked by troubleshooting structure.
            """;

        var settings = new ChunkingSettings { MaxTokensPerChunk = 50, OverlapTokens = 8 };
        var result = _sut.Chunk(text, settings: settings, sourceType: SourceType.Document);

        // Should still produce chunks, but via generic (markdown-aware) chunking.
        Assert.True(result.Count >= 1);
        // Generic chunking does not produce "Symptoms" context labels.
        var contexts = result.Select(c => c.Context).Where(c => c is not null).ToList();
        Assert.DoesNotContain("Symptoms", contexts);
    }

    [Fact]
    public void Chunk_NullSourceType_UsesGenericChunking()
    {
        var text = string.Join(" ", Enumerable.Repeat("word", 1000));
        var result = _sut.Chunk(text, sourceType: null);
        Assert.True(result.Count > 1);
    }

    [Fact]
    public void Chunk_TicketWithOversizedSection_SplitsLargeSection()
    {
        var longDescription = string.Join(" ", Enumerable.Repeat("The application experiences intermittent failures.", 100));
        var text = $"""
            Symptoms:
            {longDescription}

            Resolution:
            Fixed the timeout configuration.
            """;

        var settings = new ChunkingSettings { MaxTokensPerChunk = 80, OverlapTokens = 10 };
        var result = _sut.Chunk(text, settings: settings, sourceType: SourceType.Ticket);

        Assert.True(result.Count >= 3, "Oversized section should be sub-split");
        Assert.Equal("Symptoms", result[0].Context);
    }

    [Theory]
    [InlineData("Description:", "Symptoms")]
    [InlineData("Problem Description:", "Symptoms")]
    [InlineData("Steps to Reproduce:", "Steps to Reproduce")]
    [InlineData("Repro Steps:", "Steps to Reproduce")]
    [InlineData("Root Cause:", "Root Cause")]
    [InlineData("Root Cause Analysis:", "Root Cause")]
    [InlineData("Resolution:", "Resolution")]
    [InlineData("Fix Applied:", "Resolution")]
    [InlineData("Workaround:", "Workaround")]
    [InlineData("Verification:", "Verification")]
    [InlineData("Test Plan:", "Verification")]
    [InlineData("Impact:", "Impact")]
    [InlineData("Customer Impact:", "Impact")]
    [InlineData("Environment:", "Environment")]
    [InlineData("Additional Context:", "Environment")]
    [InlineData("Expected Behavior:", "Expected Behavior")]
    public void Chunk_TicketSectionHeaderVariants_RecognizedCorrectly(string header, string expectedContext)
    {
        // Pad sections with enough content to exceed a small max chunk size, forcing multi-chunk output.
        var padding = string.Join(" ", Enumerable.Repeat("Detailed troubleshooting information for this section.", 20));
        var text = $"""
            {header}
            {padding}

            Resolution:
            {padding}
            """;

        var settings = new ChunkingSettings { MaxTokensPerChunk = 50, OverlapTokens = 5 };
        var result = _sut.Chunk(text, settings: settings, sourceType: SourceType.Ticket);

        var contexts = result.Select(c => c.Context).Where(c => c is not null).ToList();
        Assert.Contains(expectedContext, contexts);
    }

    [Fact]
    public void Chunk_TicketStructuralBoundariesDisabled_IgnoresTicketSections()
    {
        var text = """
            Description:
            Crash on submit.

            Root Cause:
            Null reference.

            Resolution:
            Null check added.
            """;

        var settings = new ChunkingSettings
        {
            MaxTokensPerChunk = 20,
            OverlapTokens = 3,
            UseStructuralBoundaries = false,
        };
        var result = _sut.Chunk(text, settings: settings, sourceType: SourceType.Ticket);

        // With structural boundaries disabled, ticket chunking should not activate.
        var contexts = result.Select(c => c.Context).Where(c => c is not null).ToList();
        Assert.DoesNotContain("Symptoms", contexts);
    }
}
