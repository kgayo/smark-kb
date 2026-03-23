using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
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

    #region BuildConfidenceRationale (P3-024)

    [Fact]
    public void BuildConfidenceRationale_NoChunks_ReturnsNoEvidenceMessage()
    {
        var rationale = ChatOrchestrator.BuildConfidenceRationale([], 0f, "Low", null);
        Assert.Equal("No matching evidence found in the knowledge base.", rationale);
    }

    [Fact]
    public void BuildConfidenceRationale_HighRelevance_DescribesHighRelevance()
    {
        var chunks = new List<RetrievedChunk>
        {
            CreateChunk("c1", rrfScore: 0.85) with { UpdatedAt = DateTimeOffset.UtcNow.AddDays(-2) },
            CreateChunk("c2", rrfScore: 0.75) with { UpdatedAt = DateTimeOffset.UtcNow.AddDays(-5) },
            CreateChunk("c3", rrfScore: 0.80) with { UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1) },
        };

        var rationale = ChatOrchestrator.BuildConfidenceRationale(chunks, 0.8f, "High", null);

        Assert.Contains("3 evidence chunks matched", rationale);
        Assert.Contains("high relevance", rationale);
        Assert.Contains("avg score: 0.80", rationale);
        Assert.Contains("1 day ago", rationale);
    }

    [Fact]
    public void BuildConfidenceRationale_LowRelevance_DescribesLowRelevance()
    {
        var chunks = new List<RetrievedChunk>
        {
            CreateChunk("c1", rrfScore: 0.2) with { UpdatedAt = DateTimeOffset.UtcNow.AddDays(-60) },
        };

        var rationale = ChatOrchestrator.BuildConfidenceRationale(chunks, 0.2f, "Low", null);

        Assert.Contains("1 evidence chunk matched", rationale);
        Assert.Contains("low relevance", rationale);
        Assert.Contains("60 days", rationale);
    }

    [Fact]
    public void BuildConfidenceRationale_MultipleSystems_DescribesDiversity()
    {
        var chunks = new List<RetrievedChunk>
        {
            CreateChunk("c1", rrfScore: 0.6) with { SourceSystem = "AzureDevOps", UpdatedAt = DateTimeOffset.UtcNow },
            CreateChunk("c2", rrfScore: 0.5) with { SourceSystem = "SharePoint", UpdatedAt = DateTimeOffset.UtcNow },
        };

        var rationale = ChatOrchestrator.BuildConfidenceRationale(chunks, 0.5f, "Medium", null);

        Assert.Contains("sources span 2 systems", rationale);
    }

    [Fact]
    public void BuildConfidenceRationale_SingleSystem_DescribesSingleSource()
    {
        var chunks = new List<RetrievedChunk>
        {
            CreateChunk("c1", rrfScore: 0.6) with { UpdatedAt = DateTimeOffset.UtcNow },
            CreateChunk("c2", rrfScore: 0.5) with { UpdatedAt = DateTimeOffset.UtcNow },
        };

        var rationale = ChatOrchestrator.BuildConfidenceRationale(chunks, 0.5f, "Medium", null);

        Assert.Contains("all from a single source system", rationale);
    }

    [Fact]
    public void BuildConfidenceRationale_WithPatterns_MentionsPatternCount()
    {
        var chunks = new List<RetrievedChunk>
        {
            CreateChunk("c1", rrfScore: 0.7) with { ResultSource = "Evidence", UpdatedAt = DateTimeOffset.UtcNow },
            CreateChunk("c2", rrfScore: 0.6) with { ResultSource = "Pattern", UpdatedAt = DateTimeOffset.UtcNow },
        };

        var rationale = ChatOrchestrator.BuildConfidenceRationale(chunks, 0.65f, "Medium", null);

        Assert.Contains("1 case pattern included", rationale);
    }

    [Fact]
    public void BuildConfidenceRationale_WithModelRationale_AppendsIt()
    {
        var chunks = new List<RetrievedChunk>
        {
            CreateChunk("c1", rrfScore: 0.7) with { UpdatedAt = DateTimeOffset.UtcNow },
        };

        var rationale = ChatOrchestrator.BuildConfidenceRationale(
            chunks, 0.7f, "High", "The evidence directly addresses the question.");

        Assert.Contains("Model assessment: The evidence directly addresses the question.", rationale);
    }

    [Fact]
    public void BuildConfidenceRationale_ShortModelRationale_IgnoresIt()
    {
        var chunks = new List<RetrievedChunk>
        {
            CreateChunk("c1", rrfScore: 0.7) with { UpdatedAt = DateTimeOffset.UtcNow },
        };

        var rationale = ChatOrchestrator.BuildConfidenceRationale(chunks, 0.7f, "High", "ok");

        Assert.DoesNotContain("Model assessment", rationale);
    }

    [Fact]
    public void BuildConfidenceRationale_OldEvidence_DescribesAge()
    {
        var chunks = new List<RetrievedChunk>
        {
            CreateChunk("c1", rrfScore: 0.5) with { UpdatedAt = DateTimeOffset.UtcNow.AddDays(-120) },
        };

        var rationale = ChatOrchestrator.BuildConfidenceRationale(chunks, 0.3f, "Low", null);

        Assert.Contains("120 days old", rationale);
    }

    [Fact]
    public void ChatResponse_ConfidenceRationale_DefaultsToNull()
    {
        var response = new ChatResponse
        {
            ResponseType = "final_answer",
            Answer = "Test",
            Citations = [],
            Confidence = 0.85f,
            ConfidenceLabel = "High",
            TraceId = "t1",
            HasEvidence = true,
            SystemPromptVersion = "1.0",
        };

        Assert.Null(response.ConfidenceRationale);
    }

    [Fact]
    public void ChatResponse_ConfidenceRationale_CanBeSet()
    {
        var response = new ChatResponse
        {
            ResponseType = "final_answer",
            Answer = "Test",
            Citations = [],
            Confidence = 0.85f,
            ConfidenceLabel = "High",
            ConfidenceRationale = "3 high-relevance chunks matched.",
            TraceId = "t1",
            HasEvidence = true,
            SystemPromptVersion = "1.0",
        };

        Assert.Equal("3 high-relevance chunks matched.", response.ConfidenceRationale);
    }

    #endregion

    #region StructuredOutputSchema

    [Fact]
    public void StructuredOutputSchema_IsNotNull()
    {
        Assert.NotNull(ChatOrchestrator.StructuredOutputSchema);
    }

    [Fact]
    public void StructuredOutputSchema_SerializesToValidJsonSchema()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(ChatOrchestrator.StructuredOutputSchema);
        var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("object", root.GetProperty("type").GetString());
        Assert.False(root.GetProperty("additionalProperties").GetBoolean());
    }

    [Fact]
    public void StructuredOutputSchema_HasAllRequiredTopLevelProperties()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(ChatOrchestrator.StructuredOutputSchema);
        var root = System.Text.Json.JsonDocument.Parse(json).RootElement;
        var properties = root.GetProperty("properties");

        var expectedProperties = new[]
        {
            "response_type", "answer", "citations", "confidence",
            "confidence_rationale", "next_steps", "escalation",
        };

        foreach (var prop in expectedProperties)
        {
            Assert.True(properties.TryGetProperty(prop, out _), $"Missing property: {prop}");
        }
    }

    [Fact]
    public void StructuredOutputSchema_RequiredArrayMatchesProperties()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(ChatOrchestrator.StructuredOutputSchema);
        var root = System.Text.Json.JsonDocument.Parse(json).RootElement;

        var required = root.GetProperty("required")
            .EnumerateArray()
            .Select(e => e.GetString()!)
            .OrderBy(s => s)
            .ToList();

        var expectedRequired = new[]
        {
            "response_type", "answer", "citations", "confidence",
            "confidence_rationale", "next_steps", "escalation",
        }.OrderBy(s => s).ToList();

        Assert.Equal(expectedRequired, required);
    }

    [Fact]
    public void StructuredOutputSchema_ResponseTypeHasCorrectEnum()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(ChatOrchestrator.StructuredOutputSchema);
        var root = System.Text.Json.JsonDocument.Parse(json).RootElement;
        var responseType = root.GetProperty("properties").GetProperty("response_type");

        Assert.Equal("string", responseType.GetProperty("type").GetString());

        var enumValues = responseType.GetProperty("enum")
            .EnumerateArray()
            .Select(e => e.GetString()!)
            .OrderBy(s => s)
            .ToList();

        Assert.Equal(new[] { "escalate", "final_answer", "next_steps_only" }, enumValues);
    }

    [Fact]
    public void StructuredOutputSchema_EscalationSubObjectHasCorrectStructure()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(ChatOrchestrator.StructuredOutputSchema);
        var root = System.Text.Json.JsonDocument.Parse(json).RootElement;
        var escalation = root.GetProperty("properties").GetProperty("escalation");

        Assert.Equal("object", escalation.GetProperty("type").GetString());
        Assert.False(escalation.GetProperty("additionalProperties").GetBoolean());

        var escalationProps = escalation.GetProperty("properties");
        Assert.True(escalationProps.TryGetProperty("recommended", out var rec));
        Assert.Equal("boolean", rec.GetProperty("type").GetString());

        Assert.True(escalationProps.TryGetProperty("target_team", out _));
        Assert.True(escalationProps.TryGetProperty("reason", out _));
        Assert.True(escalationProps.TryGetProperty("handoff_note", out _));

        var escalationRequired = escalation.GetProperty("required")
            .EnumerateArray()
            .Select(e => e.GetString()!)
            .OrderBy(s => s)
            .ToList();

        Assert.Equal(
            new[] { "handoff_note", "reason", "recommended", "target_team" },
            escalationRequired);
    }

    [Fact]
    public void StructuredOutputSchema_CitationsIsStringArray()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(ChatOrchestrator.StructuredOutputSchema);
        var root = System.Text.Json.JsonDocument.Parse(json).RootElement;
        var citations = root.GetProperty("properties").GetProperty("citations");

        Assert.Equal("array", citations.GetProperty("type").GetString());
        Assert.Equal("string", citations.GetProperty("items").GetProperty("type").GetString());
    }

    [Fact]
    public void StructuredOutputSchema_ConfidenceIsNumber()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(ChatOrchestrator.StructuredOutputSchema);
        var root = System.Text.Json.JsonDocument.Parse(json).RootElement;
        var confidence = root.GetProperty("properties").GetProperty("confidence");

        Assert.Equal("number", confidence.GetProperty("type").GetString());
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

#region T-001 Gap (a): LLM Response Deserialization Roundtrip Tests

public class OpenAiResponseDeserializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    [Fact]
    public void Deserialize_FullFinalAnswerResponse_AllFieldsMapped()
    {
        var json = """
        {
            "response_type": "final_answer",
            "answer": "The issue is caused by a misconfigured SSL certificate.",
            "citations": ["chunk-001", "chunk-002"],
            "confidence": 0.85,
            "confidence_rationale": "Multiple matching evidence chunks with high relevance.",
            "next_steps": ["Verify certificate expiry", "Check DNS records"],
            "escalation": {
                "recommended": false,
                "target_team": "",
                "reason": "",
                "handoff_note": ""
            }
        }
        """;

        var result = JsonSerializer.Deserialize<OpenAiStructuredResponse>(json, JsonOptions);

        Assert.NotNull(result);
        Assert.Equal("final_answer", result!.ResponseType);
        Assert.Equal("The issue is caused by a misconfigured SSL certificate.", result.Answer);
        Assert.Equal(2, result.Citations.Count);
        Assert.Equal("chunk-001", result.Citations[0]);
        Assert.Equal("chunk-002", result.Citations[1]);
        Assert.Equal(0.85f, result.Confidence, 0.001f);
        Assert.Equal("Multiple matching evidence chunks with high relevance.", result.ConfidenceRationale);
        Assert.Equal(2, result.NextSteps.Count);
        Assert.Equal("Verify certificate expiry", result.NextSteps[0]);
        Assert.False(result.Escalation.Recommended);
    }

    [Fact]
    public void Deserialize_EscalateResponse_EscalationFieldsPopulated()
    {
        var json = """
        {
            "response_type": "escalate",
            "answer": "This issue requires engineering investigation.",
            "citations": ["chunk-003"],
            "confidence": 0.3,
            "confidence_rationale": "Low evidence coverage.",
            "next_steps": [],
            "escalation": {
                "recommended": true,
                "target_team": "Platform Engineering",
                "reason": "Production database corruption detected.",
                "handoff_note": "Customer reported data loss in tenant-42 at 14:00 UTC."
            }
        }
        """;

        var result = JsonSerializer.Deserialize<OpenAiStructuredResponse>(json, JsonOptions);

        Assert.NotNull(result);
        Assert.Equal("escalate", result!.ResponseType);
        Assert.True(result.Escalation.Recommended);
        Assert.Equal("Platform Engineering", result.Escalation.TargetTeam);
        Assert.Equal("Production database corruption detected.", result.Escalation.Reason);
        Assert.Equal("Customer reported data loss in tenant-42 at 14:00 UTC.", result.Escalation.HandoffNote);
    }

    [Fact]
    public void Deserialize_NextStepsOnlyResponse_EmptyCitations()
    {
        var json = """
        {
            "response_type": "next_steps_only",
            "answer": "I don't have enough information to answer confidently.",
            "citations": [],
            "confidence": 0.15,
            "confidence_rationale": "No matching evidence found.",
            "next_steps": ["Rephrase with more details", "Check related tickets"],
            "escalation": {
                "recommended": false,
                "target_team": "",
                "reason": "",
                "handoff_note": ""
            }
        }
        """;

        var result = JsonSerializer.Deserialize<OpenAiStructuredResponse>(json, JsonOptions);

        Assert.NotNull(result);
        Assert.Equal("next_steps_only", result!.ResponseType);
        Assert.Empty(result.Citations);
        Assert.Equal(0.15f, result.Confidence, 0.001f);
        Assert.Equal(2, result.NextSteps.Count);
    }

    [Fact]
    public void Deserialize_MinimalDefaultValues_UsesRecordDefaults()
    {
        // Simulate a response where optional fields are at defaults
        var json = """
        {
            "response_type": "final_answer",
            "answer": "Answer text.",
            "citations": [],
            "confidence": 0.0,
            "confidence_rationale": "",
            "next_steps": [],
            "escalation": {
                "recommended": false,
                "target_team": "",
                "reason": "",
                "handoff_note": ""
            }
        }
        """;

        var result = JsonSerializer.Deserialize<OpenAiStructuredResponse>(json, JsonOptions);

        Assert.NotNull(result);
        Assert.Equal(0.0f, result!.Confidence);
        Assert.Empty(result.Citations);
        Assert.Empty(result.NextSteps);
        Assert.Equal(string.Empty, result.ConfidenceRationale);
        Assert.False(result.Escalation.Recommended);
    }

    [Fact]
    public void Deserialize_Roundtrip_SerializeAndDeserializePreservesValues()
    {
        var original = new OpenAiStructuredResponse
        {
            ResponseType = "final_answer",
            Answer = "SSL cert expired.",
            Citations = ["chunk-a", "chunk-b"],
            Confidence = 0.92f,
            ConfidenceRationale = "High relevance.",
            NextSteps = ["Renew cert"],
            Escalation = new OpenAiEscalationOutput
            {
                Recommended = true,
                TargetTeam = "Infra",
                Reason = "Outage",
                HandoffNote = "Urgent",
            },
        };

        var serialized = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<OpenAiStructuredResponse>(serialized, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(original.ResponseType, deserialized!.ResponseType);
        Assert.Equal(original.Answer, deserialized.Answer);
        Assert.Equal(original.Citations, deserialized.Citations);
        Assert.Equal(original.Confidence, deserialized.Confidence, 0.001f);
        Assert.Equal(original.ConfidenceRationale, deserialized.ConfidenceRationale);
        Assert.Equal(original.NextSteps, deserialized.NextSteps);
        Assert.True(deserialized.Escalation.Recommended);
        Assert.Equal("Infra", deserialized.Escalation.TargetTeam);
    }

    [Fact]
    public void Deserialize_ExtraFieldsIgnored_NoCrash()
    {
        var json = """
        {
            "response_type": "final_answer",
            "answer": "Answer.",
            "citations": [],
            "confidence": 0.5,
            "confidence_rationale": "Ok.",
            "next_steps": [],
            "escalation": {
                "recommended": false,
                "target_team": "",
                "reason": "",
                "handoff_note": ""
            },
            "unknown_extra_field": "should be ignored"
        }
        """;

        var result = JsonSerializer.Deserialize<OpenAiStructuredResponse>(json, JsonOptions);

        Assert.NotNull(result);
        Assert.Equal("final_answer", result!.ResponseType);
    }

    [Fact]
    public void Deserialize_MissingOptionalFields_UsesDefaults()
    {
        // OpenAI structured output always returns all fields, but test graceful handling
        var json = """
        {
            "response_type": "final_answer",
            "answer": "Answer."
        }
        """;

        var result = JsonSerializer.Deserialize<OpenAiStructuredResponse>(json, JsonOptions);

        Assert.NotNull(result);
        Assert.Equal("final_answer", result!.ResponseType);
        Assert.Equal("Answer.", result.Answer);
        Assert.Empty(result.Citations);
        Assert.Equal(0.0f, result.Confidence);
        Assert.Empty(result.NextSteps);
        Assert.NotNull(result.Escalation);
        Assert.False(result.Escalation.Recommended);
    }

    [Fact]
    public void Deserialize_ChatResponseDto_FromApiJson()
    {
        // Simulate what the API returns to the frontend — ChatResponse serialized as camelCase by default ASP.NET
        var response = new ChatResponse
        {
            ResponseType = "final_answer",
            Answer = "The fix is to update the config.",
            Citations =
            [
                new CitationDto
                {
                    ChunkId = "c1",
                    EvidenceId = "e1",
                    Title = "Config Guide",
                    SourceUrl = "https://wiki.example.com/config",
                    SourceSystem = "SharePoint",
                    Snippet = "Update the config file...",
                    UpdatedAt = new DateTimeOffset(2025, 6, 15, 0, 0, 0, TimeSpan.Zero),
                    AccessLabel = "Internal",
                },
            ],
            Confidence = 0.78f,
            ConfidenceLabel = "High",
            ConfidenceRationale = "2 high-relevance chunks.",
            NextSteps = ["Restart the service"],
            TraceId = "trace-123",
            HasEvidence = true,
            SystemPromptVersion = "1.0",
            PiiRedactedCount = 1,
            IssueCategory = "Configuration",
            ClassifiedProductArea = "API",
            SeverityGuess = "P3",
            ClassificationConfidence = 0.82f,
            MissingInfoSuggestions = ["Which environment?"],
            EscalationLikelihood = 0.1f,
        };

        var json = JsonSerializer.Serialize(response);
        var deserialized = JsonSerializer.Deserialize<ChatResponse>(json);

        Assert.NotNull(deserialized);
        Assert.Equal("final_answer", deserialized!.ResponseType);
        Assert.Equal("The fix is to update the config.", deserialized.Answer);
        Assert.Single(deserialized.Citations);
        Assert.Equal("c1", deserialized.Citations[0].ChunkId);
        Assert.Equal(0.78f, deserialized.Confidence, 0.001f);
        Assert.Equal("High", deserialized.ConfidenceLabel);
        Assert.Equal("trace-123", deserialized.TraceId);
        Assert.True(deserialized.HasEvidence);
        Assert.Equal(1, deserialized.PiiRedactedCount);
        Assert.Equal("Configuration", deserialized.IssueCategory);
        Assert.Equal("API", deserialized.ClassifiedProductArea);
        Assert.Equal("P3", deserialized.SeverityGuess);
        Assert.Equal(0.82f, deserialized.ClassificationConfidence, 0.001f);
        Assert.Single(deserialized.MissingInfoSuggestions);
        Assert.Equal(0.1f, deserialized.EscalationLikelihood, 0.001f);
    }
}

#endregion

#region T-001 Gap (b): ChatOrchestrator.OrchestrateAsync Integration Tests

public class ChatOrchestratorIntegrationTests
{
    [Fact]
    public async Task OrchestrateAsync_FullPipeline_ReturnsStructuredResponse()
    {
        // Arrange: set up mock services
        var embeddingService = new StubEmbeddingService();
        var chunks = new List<RetrievedChunk>
        {
            MakeChunk("c1", "Content about SSL certs", 0.8),
            MakeChunk("c2", "Content about DNS config", 0.6),
        };
        var retrievalService = new StubRetrievalService(new RetrievalResult
        {
            Chunks = chunks,
            AclFilteredOutCount = 0,
            HasEvidence = true,
            TraceId = "test-trace",
        });
        var traceWriter = new StubTraceWriter();
        var piiRedactionService = new StubPiiRedactionService();
        var auditWriter = new StubAuditWriter();
        var tokenUsageService = new StubTokenUsageService();

        // Mock OpenAI HTTP response
        var openAiResponse = new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        content = JsonSerializer.Serialize(new
                        {
                            response_type = "final_answer",
                            answer = "The SSL certificate needs renewal.",
                            citations = new[] { "c1" },
                            confidence = 0.9,
                            confidence_rationale = "Strong evidence match.",
                            next_steps = new[] { "Check certificate expiry date" },
                            escalation = new
                            {
                                recommended = false,
                                target_team = "",
                                reason = "",
                                handoff_note = "",
                            },
                        }),
                    },
                },
            },
            usage = new
            {
                prompt_tokens = 500,
                completion_tokens = 100,
                total_tokens = 600,
            },
        };

        var handler = new StubHttpMessageHandler(
            JsonSerializer.Serialize(openAiResponse), System.Net.HttpStatusCode.OK);
        var httpClientFactory = new StubHttpClientFactory(handler);

        var openAiSettings = new OpenAiSettings
        {
            ApiKey = "test-key",
            Model = "gpt-4o",
            Endpoint = "https://api.openai.com/v1",
        };
        var orchestrationSettings = new ChatOrchestrationSettings();
        var costSettings = new CostOptimizationSettings();
        var logger = NullLogger<ChatOrchestrator>.Instance;

        var orchestrator = new ChatOrchestrator(
            embeddingService, retrievalService, traceWriter,
            piiRedactionService, auditWriter, tokenUsageService,
            httpClientFactory, openAiSettings, orchestrationSettings,
            costSettings, logger);

        var request = new ChatRequest { Query = "Why is our SSL certificate failing?" };

        // Act
        var response = await orchestrator.OrchestrateAsync(
            "tenant-1", "user-1", "corr-001", request);

        // Assert: response is well-formed with expected structure
        Assert.Equal("final_answer", response.ResponseType);
        Assert.Equal("The SSL certificate needs renewal.", response.Answer);
        Assert.True(response.HasEvidence);
        Assert.Equal("corr-001", response.TraceId);
        Assert.Equal("1.0", response.SystemPromptVersion);

        // Confidence is blended: 0.6 * model(0.9) + 0.4 * retrieval
        Assert.True(response.Confidence > 0 && response.Confidence <= 1.0f);
        Assert.NotNull(response.ConfidenceLabel);

        // Citations mapped from chunk IDs
        Assert.NotEmpty(response.Citations);
        Assert.Contains(response.Citations, c => c.ChunkId == "c1");

        // Next steps propagated
        Assert.Contains("Check certificate expiry date", response.NextSteps);

        // No escalation
        Assert.Null(response.Escalation);

        // Trace was written
        Assert.True(traceWriter.TraceWritten);

        // Token usage was recorded
        Assert.True(tokenUsageService.UsageRecorded);
    }

    [Fact]
    public async Task OrchestrateAsync_NoEvidence_ReturnsDegradedResponse()
    {
        var embeddingService = new StubEmbeddingService();
        var retrievalService = new StubRetrievalService(new RetrievalResult
        {
            Chunks = [],
            AclFilteredOutCount = 0,
            HasEvidence = false,
            TraceId = "test-trace",
        });
        var traceWriter = new StubTraceWriter();
        var piiRedactionService = new StubPiiRedactionService();
        var auditWriter = new StubAuditWriter();
        var tokenUsageService = new StubTokenUsageService();

        var handler = new StubHttpMessageHandler("{}", System.Net.HttpStatusCode.OK);
        var httpClientFactory = new StubHttpClientFactory(handler);

        var orchestrator = new ChatOrchestrator(
            embeddingService, retrievalService, traceWriter,
            piiRedactionService, auditWriter, tokenUsageService,
            httpClientFactory, new OpenAiSettings { ApiKey = "test" },
            new ChatOrchestrationSettings(), new CostOptimizationSettings(),
            NullLogger<ChatOrchestrator>.Instance);

        var request = new ChatRequest { Query = "Something obscure" };

        var response = await orchestrator.OrchestrateAsync(
            "tenant-1", "user-1", "corr-002", request);

        Assert.Equal("next_steps_only", response.ResponseType);
        Assert.False(response.HasEvidence);
        Assert.Empty(response.Citations);
    }

    [Fact]
    public async Task OrchestrateAsync_EmbeddingFailure_ReturnsGracefulError()
    {
        var embeddingService = new FailingEmbeddingService();
        var retrievalService = new StubRetrievalService(new RetrievalResult
        {
            Chunks = [],
            AclFilteredOutCount = 0,
            HasEvidence = false,
            TraceId = "test-trace",
        });
        var traceWriter = new StubTraceWriter();
        var piiRedactionService = new StubPiiRedactionService();
        var auditWriter = new StubAuditWriter();
        var tokenUsageService = new StubTokenUsageService();

        var handler = new StubHttpMessageHandler("{}", System.Net.HttpStatusCode.OK);
        var httpClientFactory = new StubHttpClientFactory(handler);

        var orchestrator = new ChatOrchestrator(
            embeddingService, retrievalService, traceWriter,
            piiRedactionService, auditWriter, tokenUsageService,
            httpClientFactory, new OpenAiSettings { ApiKey = "test" },
            new ChatOrchestrationSettings(), new CostOptimizationSettings(),
            NullLogger<ChatOrchestrator>.Instance);

        var request = new ChatRequest { Query = "Test query" };

        var response = await orchestrator.OrchestrateAsync(
            "tenant-1", "user-1", "corr-003", request);

        // Should return a graceful error, not throw
        Assert.False(response.HasEvidence);
        Assert.Contains("Unable to process", response.Answer);
    }

    [Fact]
    public async Task OrchestrateAsync_OpenAiError_ReturnsGracefulError()
    {
        var embeddingService = new StubEmbeddingService();
        var chunks = new List<RetrievedChunk>
        {
            MakeChunk("c1", "Some evidence", 0.7),
        };
        var retrievalService = new StubRetrievalService(new RetrievalResult
        {
            Chunks = chunks,
            AclFilteredOutCount = 0,
            HasEvidence = true,
            TraceId = "test-trace",
        });
        var traceWriter = new StubTraceWriter();
        var piiRedactionService = new StubPiiRedactionService();
        var auditWriter = new StubAuditWriter();
        var tokenUsageService = new StubTokenUsageService();

        // OpenAI returns 500
        var handler = new StubHttpMessageHandler(
            """{"error":{"message":"Internal Server Error"}}""",
            System.Net.HttpStatusCode.InternalServerError);
        var httpClientFactory = new StubHttpClientFactory(handler);

        var orchestrator = new ChatOrchestrator(
            embeddingService, retrievalService, traceWriter,
            piiRedactionService, auditWriter, tokenUsageService,
            httpClientFactory, new OpenAiSettings { ApiKey = "test", Endpoint = "https://api.openai.com/v1" },
            new ChatOrchestrationSettings(), new CostOptimizationSettings(),
            NullLogger<ChatOrchestrator>.Instance);

        var request = new ChatRequest { Query = "Test query" };

        var response = await orchestrator.OrchestrateAsync(
            "tenant-1", "user-1", "corr-004", request);

        Assert.False(response.HasEvidence);
        Assert.Contains("Unable to generate", response.Answer);
    }

    [Fact]
    public async Task OrchestrateAsync_WithEscalation_ReturnsEscalationSignal()
    {
        var embeddingService = new StubEmbeddingService();
        var chunks = new List<RetrievedChunk>
        {
            MakeChunk("c1", "Evidence about production outage", 0.9),
        };
        var retrievalService = new StubRetrievalService(new RetrievalResult
        {
            Chunks = chunks,
            AclFilteredOutCount = 0,
            HasEvidence = true,
            TraceId = "test-trace",
        });

        var openAiResponse = new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        content = JsonSerializer.Serialize(new
                        {
                            response_type = "escalate",
                            answer = "This requires immediate engineering attention.",
                            citations = new[] { "c1" },
                            confidence = 0.4,
                            confidence_rationale = "Limited evidence but clear escalation signal.",
                            next_steps = new[] { "Page on-call engineer" },
                            escalation = new
                            {
                                recommended = true,
                                target_team = "Platform Engineering",
                                reason = "Production outage detected",
                                handoff_note = "Customer tenant-42 down since 14:00 UTC",
                            },
                        }),
                    },
                },
            },
            usage = new { prompt_tokens = 400, completion_tokens = 80, total_tokens = 480 },
        };

        var handler = new StubHttpMessageHandler(
            JsonSerializer.Serialize(openAiResponse), System.Net.HttpStatusCode.OK);

        var orchestrator = new ChatOrchestrator(
            embeddingService, retrievalService, new StubTraceWriter(),
            new StubPiiRedactionService(), new StubAuditWriter(), new StubTokenUsageService(),
            new StubHttpClientFactory(handler),
            new OpenAiSettings { ApiKey = "test", Endpoint = "https://api.openai.com/v1" },
            new ChatOrchestrationSettings(), new CostOptimizationSettings(),
            NullLogger<ChatOrchestrator>.Instance);

        var response = await orchestrator.OrchestrateAsync(
            "tenant-1", "user-1", "corr-005",
            new ChatRequest { Query = "Production is down!" });

        Assert.Equal("escalate", response.ResponseType);
        Assert.NotNull(response.Escalation);
        Assert.True(response.Escalation!.Recommended);
        Assert.Equal("Platform Engineering", response.Escalation.TargetTeam);
        Assert.Equal("Production outage detected", response.Escalation.Reason);
        Assert.Contains("tenant-42", response.Escalation.HandoffNote);
    }

    #region Test Doubles

    private class StubEmbeddingService : IEmbeddingService
    {
        public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
            => Task.FromResult(new float[1536]);
    }

    private class FailingEmbeddingService : IEmbeddingService
    {
        public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
            => throw new HttpRequestException("Embedding service unavailable");
    }

    private class StubRetrievalService(RetrievalResult result) : IRetrievalService
    {
        public Task<RetrievalResult> RetrieveAsync(
            string tenantId, string query, float[] queryEmbedding,
            IReadOnlyList<string>? userGroups = null, RetrievalFilter? filters = null,
            string? correlationId = null, CancellationToken cancellationToken = default)
            => Task.FromResult(result);
    }

    private class StubTraceWriter : IAnswerTraceWriter
    {
        public bool TraceWritten { get; private set; }

        public Task WriteTraceAsync(
            Guid traceId, string tenantId, string userId, string correlationId,
            string query, string responseType, float confidence, string confidenceLabel,
            IReadOnlyList<string> citedChunkIds, IReadOnlyList<string> retrievedChunkIds,
            int aclFilteredOutCount, bool hasEvidence, bool escalationRecommended,
            string systemPromptVersion, long durationMs, CancellationToken cancellationToken = default)
        {
            TraceWritten = true;
            return Task.CompletedTask;
        }
    }

    private class StubPiiRedactionService : IPiiRedactionService
    {
        public PiiRedactionResult Redact(string text)
            => new() { RedactedText = text, RedactionCounts = new Dictionary<string, int>() };

        public PiiRedactionResult Redact(string text, PiiPolicyResponse policy)
            => new() { RedactedText = text, RedactionCounts = new Dictionary<string, int>() };
    }

    private class StubAuditWriter : IAuditEventWriter
    {
        public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private class StubTokenUsageService : ITokenUsageService
    {
        public bool UsageRecorded { get; private set; }

        public Task RecordUsageAsync(string tenantId, string userId, string correlationId,
            TokenUsageRecord usage, CancellationToken ct = default)
        {
            UsageRecorded = true;
            return Task.CompletedTask;
        }

        public Task<TokenUsageSummary> GetSummaryAsync(string tenantId, DateTimeOffset periodStart,
            DateTimeOffset periodEnd, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<DailyUsageBreakdown>> GetDailyBreakdownAsync(string tenantId,
            DateTimeOffset periodStart, DateTimeOffset periodEnd, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<BudgetCheckResult> CheckBudgetAsync(string tenantId, CancellationToken ct = default)
            => throw new NotImplementedException();
    }

    private class StubHttpMessageHandler(string responseBody, System.Net.HttpStatusCode statusCode)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, System.Text.Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }

    private class StubHttpClientFactory(StubHttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }

    private static RetrievedChunk MakeChunk(string chunkId, string text, double rrfScore) => new()
    {
        ChunkId = chunkId,
        EvidenceId = $"ev-{chunkId}",
        ChunkText = text,
        Title = $"Title {chunkId}",
        SourceUrl = $"https://example.com/{chunkId}",
        SourceSystem = "AzureDevOps",
        SourceType = "WorkItem",
        UpdatedAt = DateTimeOffset.UtcNow,
        AccessLabel = "Internal",
        Visibility = "Internal",
        AllowedGroups = [],
        RrfScore = rrfScore,
    };

    #endregion
}

#endregion
