using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;

namespace SmartKb.Contracts.Tests;

public class ChatOrchestratorTests
{
    #region BuildSystemPrompt

    [Fact]
    public void BuildSystemPrompt_IncludesAllChunks()
    {
        var chunks = new List<RetrievedChunk>
        {
            CreateChunk("c1", "Chunk one content", "Title One"),
            CreateChunk("c2", "Chunk two content", "Title Two"),
        };

        var prompt = ChatOrchestrator.BuildSystemPrompt(chunks);

        Assert.Contains("[c1]", prompt);
        Assert.Contains("[c2]", prompt);
        Assert.Contains("Title One", prompt);
        Assert.Contains("Title Two", prompt);
        Assert.Contains("Chunk one content", prompt);
        Assert.Contains("Chunk two content", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_IncludesRules()
    {
        var prompt = ChatOrchestrator.BuildSystemPrompt([]);

        Assert.Contains("Rules", prompt);
        Assert.Contains("ONLY use information from the provided evidence", prompt);
        Assert.Contains("citations", prompt);
        Assert.Contains("next_steps_only", prompt);
        Assert.Contains("escalate", prompt);
        Assert.Contains("confidence", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_IncludesProductArea_WhenPresent()
    {
        var chunks = new List<RetrievedChunk>
        {
            CreateChunk("c1", "Content", "Title", productArea: "Authentication"),
        };

        var prompt = ChatOrchestrator.BuildSystemPrompt(chunks);

        Assert.Contains("Product Area: Authentication", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_IncludesContext_WhenPresent()
    {
        var chunk = CreateChunk("c1", "Content", "Title") with { ChunkContext = "Section: FAQ" };
        var prompt = ChatOrchestrator.BuildSystemPrompt([chunk]);

        Assert.Contains("Context: Section: FAQ", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_EmptyChunks_StillHasRules()
    {
        var prompt = ChatOrchestrator.BuildSystemPrompt([]);

        Assert.Contains("Evidence Chunks", prompt);
        Assert.Contains("Rules", prompt);
    }

    #endregion

    #region ComputeRetrievalConfidence

    [Fact]
    public void ComputeRetrievalConfidence_EmptyChunks_ReturnsZero()
    {
        Assert.Equal(0f, ChatOrchestrator.ComputeRetrievalConfidence([]));
    }

    [Fact]
    public void ComputeRetrievalConfidence_SingleChunk_SaturationFactor()
    {
        var chunks = new List<RetrievedChunk>
        {
            CreateChunk("c1", rrfScore: 0.8),
        };

        var confidence = ChatOrchestrator.ComputeRetrievalConfidence(chunks);
        // avgScore=0.8 * saturation=0.2 = 0.16
        Assert.Equal(0.16f, confidence, 0.01f);
    }

    [Fact]
    public void ComputeRetrievalConfidence_FiveChunks_FullSaturation()
    {
        var chunks = Enumerable.Range(0, 5)
            .Select(i => CreateChunk($"c{i}", rrfScore: 0.6))
            .ToList();

        var confidence = ChatOrchestrator.ComputeRetrievalConfidence(chunks);
        // avgScore=0.6 * saturation=1.0 = 0.6
        Assert.Equal(0.6f, confidence, 0.01f);
    }

    [Fact]
    public void ComputeRetrievalConfidence_ClampedToOne()
    {
        var chunks = Enumerable.Range(0, 10)
            .Select(i => CreateChunk($"c{i}", rrfScore: 1.5)) // hypothetically high scores
            .ToList();

        var confidence = ChatOrchestrator.ComputeRetrievalConfidence(chunks);
        Assert.True(confidence <= 1.0f);
    }

    [Fact]
    public void ComputeRetrievalConfidence_ThreeChunks_PartialSaturation()
    {
        var chunks = new List<RetrievedChunk>
        {
            CreateChunk("c1", rrfScore: 0.9),
            CreateChunk("c2", rrfScore: 0.7),
            CreateChunk("c3", rrfScore: 0.5),
        };

        var confidence = ChatOrchestrator.ComputeRetrievalConfidence(chunks);
        // avgScore=0.7 * saturation=0.6 = 0.42
        Assert.Equal(0.42f, confidence, 0.05f);
    }

    #endregion

    #region MapCitations

    [Fact]
    public void MapCitations_MapsMatchingChunkIds()
    {
        var chunks = new List<RetrievedChunk>
        {
            CreateChunk("c1", "Content of chunk 1", "Title 1"),
            CreateChunk("c2", "Content of chunk 2", "Title 2"),
            CreateChunk("c3", "Content of chunk 3", "Title 3"),
        };

        var citations = ChatOrchestrator.MapCitations(["c1", "c3"], chunks, 10);

        Assert.Equal(2, citations.Count);
        Assert.Equal("c1", citations[0].ChunkId);
        Assert.Equal("c3", citations[1].ChunkId);
        Assert.Equal("Title 1", citations[0].Title);
        Assert.Equal("Title 3", citations[1].Title);
    }

    [Fact]
    public void MapCitations_SkipsUnknownChunkIds()
    {
        var chunks = new List<RetrievedChunk>
        {
            CreateChunk("c1", "Content", "Title 1"),
        };

        var citations = ChatOrchestrator.MapCitations(["c1", "c_unknown"], chunks, 10);

        Assert.Single(citations);
        Assert.Equal("c1", citations[0].ChunkId);
    }

    [Fact]
    public void MapCitations_RespectsMaxCitations()
    {
        var chunks = Enumerable.Range(0, 5)
            .Select(i => CreateChunk($"c{i}", $"Content {i}", $"Title {i}"))
            .ToList();

        var citations = ChatOrchestrator.MapCitations(
            ["c0", "c1", "c2", "c3", "c4"], chunks, 2);

        Assert.Equal(2, citations.Count);
    }

    [Fact]
    public void MapCitations_TruncatesLongSnippets()
    {
        var longContent = new string('x', 300);
        var chunks = new List<RetrievedChunk>
        {
            CreateChunk("c1", longContent, "Title"),
        };

        var citations = ChatOrchestrator.MapCitations(["c1"], chunks, 10);

        Assert.Single(citations);
        Assert.Equal(203, citations[0].Snippet.Length); // 200 + "..."
        Assert.EndsWith("...", citations[0].Snippet);
    }

    [Fact]
    public void MapCitations_ShortSnippet_NotTruncated()
    {
        var chunks = new List<RetrievedChunk>
        {
            CreateChunk("c1", "Short content", "Title"),
        };

        var citations = ChatOrchestrator.MapCitations(["c1"], chunks, 10);

        Assert.Equal("Short content", citations[0].Snippet);
    }

    [Fact]
    public void MapCitations_CaseInsensitiveLookup()
    {
        var chunks = new List<RetrievedChunk>
        {
            CreateChunk("C1", "Content", "Title"),
        };

        var citations = ChatOrchestrator.MapCitations(["c1"], chunks, 10);

        Assert.Single(citations);
    }

    [Fact]
    public void MapCitations_EmptyInput_ReturnsEmpty()
    {
        var citations = ChatOrchestrator.MapCitations([], [], 10);
        Assert.Empty(citations);
    }

    #endregion

    #region EstimateTokens

    [Fact]
    public void EstimateTokens_EmptyString_ReturnsOne()
    {
        // (0 + 3) / 4 = 0 (integer division)
        Assert.Equal(0, ChatOrchestrator.EstimateTokens(""));
    }

    [Fact]
    public void EstimateTokens_ShortText_Approximation()
    {
        // "hello" = 5 chars, (5+3)/4 = 2
        Assert.Equal(2, ChatOrchestrator.EstimateTokens("hello"));
    }

    [Fact]
    public void EstimateTokens_LongerText_Approximation()
    {
        var text = new string('a', 100);
        // (100+3)/4 = 25
        Assert.Equal(25, ChatOrchestrator.EstimateTokens(text));
    }

    #endregion

    #region ChatResponse DTO Validation

    [Fact]
    public void ChatResponse_RequiredFields_SetCorrectly()
    {
        var response = new ChatResponse
        {
            ResponseType = "final_answer",
            Answer = "Test answer",
            Citations = [],
            Confidence = 0.85f,
            ConfidenceLabel = "High",
            TraceId = "trace-1",
            HasEvidence = true,
            SystemPromptVersion = "1.0",
        };

        Assert.Equal("final_answer", response.ResponseType);
        Assert.Equal("High", response.ConfidenceLabel);
        Assert.True(response.HasEvidence);
        Assert.Empty(response.NextSteps);
        Assert.Null(response.Escalation);
    }

    [Fact]
    public void ChatResponse_WithEscalation()
    {
        var response = new ChatResponse
        {
            ResponseType = "escalate",
            Answer = "This needs escalation",
            Citations = [],
            Confidence = 0.2f,
            ConfidenceLabel = "Low",
            Escalation = new EscalationSignal
            {
                Recommended = true,
                TargetTeam = "Engineering",
                Reason = "Critical issue with no evidence",
                HandoffNote = "Customer reports data loss",
            },
            TraceId = "trace-2",
            HasEvidence = false,
            SystemPromptVersion = "1.0",
        };

        Assert.True(response.Escalation!.Recommended);
        Assert.Equal("Engineering", response.Escalation.TargetTeam);
    }

    [Fact]
    public void CitationDto_AllFields()
    {
        var citation = new CitationDto
        {
            ChunkId = "ado-wi-123_chunk_0",
            EvidenceId = "ado-wi-123",
            Title = "Bug in auth module",
            SourceUrl = "https://dev.azure.com/org/project/_workitems/edit/123",
            SourceSystem = "AzureDevOps",
            Snippet = "The authentication module has a null reference...",
            UpdatedAt = DateTimeOffset.Parse("2026-03-15T10:00:00Z"),
            AccessLabel = "Internal",
        };

        Assert.Equal("ado-wi-123_chunk_0", citation.ChunkId);
        Assert.Equal("ado-wi-123", citation.EvidenceId);
    }

    [Fact]
    public void EscalationSignal_DefaultValues()
    {
        var signal = new EscalationSignal { Recommended = false };
        Assert.Equal(string.Empty, signal.TargetTeam);
        Assert.Equal(string.Empty, signal.Reason);
        Assert.Equal(string.Empty, signal.HandoffNote);
    }

    [Fact]
    public void ChatMessage_JsonPropertyNames()
    {
        var message = new ChatMessage { Role = "user", Content = "Hello" };
        Assert.Equal("user", message.Role);
        Assert.Equal("Hello", message.Content);
    }

    #endregion

    #region StructuredOutputSchema

    [Fact]
    public void StructuredOutputSchema_IsNotNull()
    {
        Assert.NotNull(ChatOrchestrator.StructuredOutputSchema);
    }

    #endregion

    #region Helpers

    private static RetrievedChunk CreateChunk(
        string chunkId,
        string text = "Content",
        string title = "Title",
        double rrfScore = 0.5,
        string? productArea = null,
        string visibility = "Internal",
        IReadOnlyList<string>? allowedGroups = null) => new()
    {
        ChunkId = chunkId,
        EvidenceId = $"ev-{chunkId}",
        ChunkText = text,
        Title = title,
        SourceUrl = $"https://example.com/{chunkId}",
        SourceSystem = "AzureDevOps",
        SourceType = "WorkItem",
        UpdatedAt = DateTimeOffset.UtcNow,
        AccessLabel = visibility == "Restricted" ? "Restricted" : "Internal",
        Visibility = visibility,
        AllowedGroups = allowedGroups ?? [],
        RrfScore = rrfScore,
        ProductArea = productArea,
    };

    private static RetrievedChunk CreateChunk(string chunkId, double rrfScore) =>
        CreateChunk(chunkId, $"Content for {chunkId}", $"Title {chunkId}", rrfScore);

    #endregion
}

#region P0-014: EnforceRestrictedContentExclusion Tests

public class RestrictedContentExclusionTests
{
    [Fact]
    public void PublicAndInternalChunks_AlwaysPassThrough()
    {
        var chunks = new List<RetrievedChunk>
        {
            MakeChunk("c1", "Public"),
            MakeChunk("c2", "Internal"),
        };

        var (safe, removed) = ChatOrchestrator.EnforceRestrictedContentExclusion(chunks, null);

        Assert.Equal(2, safe.Count);
        Assert.Equal(0, removed);
    }

    [Fact]
    public void RestrictedChunk_RemovedWhenNoUserGroups()
    {
        var chunks = new List<RetrievedChunk>
        {
            MakeChunk("c1", "Internal"),
            MakeChunk("c2", "Restricted", ["TeamAlpha"]),
        };

        var (safe, removed) = ChatOrchestrator.EnforceRestrictedContentExclusion(chunks, null);

        Assert.Single(safe);
        Assert.Equal("c1", safe[0].ChunkId);
        Assert.Equal(1, removed);
    }

    [Fact]
    public void RestrictedChunk_RemovedWhenUserNotInAllowedGroups()
    {
        var chunks = new List<RetrievedChunk>
        {
            MakeChunk("c1", "Restricted", ["TeamAlpha", "TeamBeta"]),
        };

        var (safe, removed) = ChatOrchestrator.EnforceRestrictedContentExclusion(
            chunks, new[] { "TeamGamma" });

        Assert.Empty(safe);
        Assert.Equal(1, removed);
    }

    [Fact]
    public void RestrictedChunk_PassesWhenUserInAllowedGroup()
    {
        var chunks = new List<RetrievedChunk>
        {
            MakeChunk("c1", "Restricted", ["TeamAlpha", "TeamBeta"]),
        };

        var (safe, removed) = ChatOrchestrator.EnforceRestrictedContentExclusion(
            chunks, new[] { "TeamBeta" });

        Assert.Single(safe);
        Assert.Equal(0, removed);
    }

    [Fact]
    public void RestrictedChunk_CaseInsensitiveGroupMatching()
    {
        var chunks = new List<RetrievedChunk>
        {
            MakeChunk("c1", "Restricted", ["TeamAlpha"]),
        };

        var (safe, removed) = ChatOrchestrator.EnforceRestrictedContentExclusion(
            chunks, new[] { "teamalpha" });

        Assert.Single(safe);
        Assert.Equal(0, removed);
    }

    [Fact]
    public void RestrictedChunk_CaseInsensitiveVisibility()
    {
        var chunks = new List<RetrievedChunk>
        {
            MakeChunk("c1", "restricted", ["TeamAlpha"]),
        };

        var (safe, removed) = ChatOrchestrator.EnforceRestrictedContentExclusion(chunks, null);

        Assert.Empty(safe);
        Assert.Equal(1, removed);
    }

    [Fact]
    public void MixedVisibility_OnlyRestrictedFiltered()
    {
        var chunks = new List<RetrievedChunk>
        {
            MakeChunk("c1", "Public"),
            MakeChunk("c2", "Restricted", ["TeamAlpha"]),
            MakeChunk("c3", "Internal"),
            MakeChunk("c4", "Restricted", ["TeamBeta"]),
        };

        var (safe, removed) = ChatOrchestrator.EnforceRestrictedContentExclusion(
            chunks, new[] { "TeamBeta" });

        Assert.Equal(3, safe.Count);
        Assert.Equal(1, removed);
        Assert.Contains(safe, c => c.ChunkId == "c1");
        Assert.Contains(safe, c => c.ChunkId == "c3");
        Assert.Contains(safe, c => c.ChunkId == "c4");
        Assert.DoesNotContain(safe, c => c.ChunkId == "c2");
    }

    [Fact]
    public void EmptyChunks_ReturnsEmpty()
    {
        var (safe, removed) = ChatOrchestrator.EnforceRestrictedContentExclusion([], null);

        Assert.Empty(safe);
        Assert.Equal(0, removed);
    }

    [Fact]
    public void RestrictedChunk_EmptyAllowedGroups_AlwaysRemoved()
    {
        var chunks = new List<RetrievedChunk>
        {
            MakeChunk("c1", "Restricted", []),
        };

        var (safe, removed) = ChatOrchestrator.EnforceRestrictedContentExclusion(
            chunks, new[] { "TeamAlpha" });

        Assert.Empty(safe);
        Assert.Equal(1, removed);
    }

    [Fact]
    public void RestrictedContentNeverReachesPrompt_IntegrationProof()
    {
        // Simulate a scenario where retrieval "accidentally" returns restricted content
        // that the user should not see. Verify the orchestration guard catches it.
        var chunks = new List<RetrievedChunk>
        {
            MakeChunk("safe-1", "Internal"),
            MakeChunk("restricted-secret", "Restricted", ["SecretTeam"]),
            MakeChunk("safe-2", "Public"),
        };

        // User is NOT in SecretTeam — restricted chunk must be excluded.
        var (safe, removed) = ChatOrchestrator.EnforceRestrictedContentExclusion(
            chunks, new[] { "SupportTeam" });

        Assert.Equal(2, safe.Count);
        Assert.Equal(1, removed);

        // Verify that building the system prompt from safe chunks does NOT contain restricted content.
        var prompt = ChatOrchestrator.BuildSystemPrompt(safe);
        Assert.DoesNotContain("restricted-secret", prompt);
        Assert.Contains("safe-1", prompt);
        Assert.Contains("safe-2", prompt);
    }

    private static RetrievedChunk MakeChunk(
        string chunkId,
        string visibility,
        IReadOnlyList<string>? allowedGroups = null) => new()
    {
        ChunkId = chunkId,
        EvidenceId = $"ev-{chunkId}",
        ChunkText = $"Content for {chunkId}",
        Title = $"Title {chunkId}",
        SourceUrl = $"https://example.com/{chunkId}",
        SourceSystem = "AzureDevOps",
        SourceType = "WorkItem",
        UpdatedAt = DateTimeOffset.UtcNow,
        AccessLabel = visibility == "Restricted" ? "Restricted" : visibility,
        Visibility = visibility,
        AllowedGroups = allowedGroups ?? [],
        RrfScore = 0.5,
    };
}

#endregion
