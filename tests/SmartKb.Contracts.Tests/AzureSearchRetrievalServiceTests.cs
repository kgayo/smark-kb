using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;

namespace SmartKb.Contracts.Tests;

public class AzureSearchRetrievalServiceTests
{
    #region ACL Filtering

    [Fact]
    public void ApplyAclFilter_PublicDocuments_AlwaysPassThrough()
    {
        var results = new List<RawSearchResult>
        {
            CreateRawResult("c1", visibility: "Public"),
            CreateRawResult("c2", visibility: "Public"),
        };

        var (filtered, filteredOut) = AzureSearchRetrievalService.ApplyAclFilter(results, null);

        Assert.Equal(2, filtered.Count);
        Assert.Equal(0, filteredOut);
    }

    [Fact]
    public void ApplyAclFilter_InternalDocuments_AlwaysPassThrough()
    {
        var results = new List<RawSearchResult>
        {
            CreateRawResult("c1", visibility: "Internal"),
            CreateRawResult("c2", visibility: "Internal"),
        };

        var (filtered, filteredOut) = AzureSearchRetrievalService.ApplyAclFilter(results, null);

        Assert.Equal(2, filtered.Count);
        Assert.Equal(0, filteredOut);
    }

    [Fact]
    public void ApplyAclFilter_RestrictedDocuments_FilteredWhenNoUserGroups()
    {
        var results = new List<RawSearchResult>
        {
            CreateRawResult("c1", visibility: "Restricted", allowedGroups: ["team-a"]),
        };

        var (filtered, filteredOut) = AzureSearchRetrievalService.ApplyAclFilter(results, null);

        Assert.Empty(filtered);
        Assert.Equal(1, filteredOut);
    }

    [Fact]
    public void ApplyAclFilter_RestrictedDocuments_FilteredWhenEmptyUserGroups()
    {
        var results = new List<RawSearchResult>
        {
            CreateRawResult("c1", visibility: "Restricted", allowedGroups: ["team-a"]),
        };

        var (filtered, filteredOut) = AzureSearchRetrievalService.ApplyAclFilter(results, []);

        Assert.Empty(filtered);
        Assert.Equal(1, filteredOut);
    }

    [Fact]
    public void ApplyAclFilter_RestrictedDocuments_PassWhenUserInAllowedGroup()
    {
        var results = new List<RawSearchResult>
        {
            CreateRawResult("c1", visibility: "Restricted", allowedGroups: ["team-a", "team-b"]),
        };

        var (filtered, filteredOut) = AzureSearchRetrievalService.ApplyAclFilter(results, ["team-b"]);

        Assert.Single(filtered);
        Assert.Equal(0, filteredOut);
    }

    [Fact]
    public void ApplyAclFilter_RestrictedDocuments_FilteredWhenUserNotInAnyGroup()
    {
        var results = new List<RawSearchResult>
        {
            CreateRawResult("c1", visibility: "Restricted", allowedGroups: ["team-a", "team-b"]),
        };

        var (filtered, filteredOut) = AzureSearchRetrievalService.ApplyAclFilter(results, ["team-c"]);

        Assert.Empty(filtered);
        Assert.Equal(1, filteredOut);
    }

    [Fact]
    public void ApplyAclFilter_MixedVisibility_CorrectCounts()
    {
        var results = new List<RawSearchResult>
        {
            CreateRawResult("c1", visibility: "Public"),
            CreateRawResult("c2", visibility: "Internal"),
            CreateRawResult("c3", visibility: "Restricted", allowedGroups: ["team-a"]),
            CreateRawResult("c4", visibility: "Restricted", allowedGroups: ["team-b"]),
            CreateRawResult("c5", visibility: "Internal"),
        };

        var (filtered, filteredOut) = AzureSearchRetrievalService.ApplyAclFilter(results, ["team-a"]);

        Assert.Equal(4, filtered.Count);
        Assert.Equal(1, filteredOut);
        Assert.DoesNotContain(filtered, r => r.ChunkId == "c4");
    }

    [Fact]
    public void ApplyAclFilter_CaseInsensitiveGroupMatching()
    {
        var results = new List<RawSearchResult>
        {
            CreateRawResult("c1", visibility: "Restricted", allowedGroups: ["Team-A"]),
        };

        var (filtered, filteredOut) = AzureSearchRetrievalService.ApplyAclFilter(results, ["team-a"]);

        Assert.Single(filtered);
        Assert.Equal(0, filteredOut);
    }

    [Fact]
    public void ApplyAclFilter_CaseInsensitiveVisibility()
    {
        var results = new List<RawSearchResult>
        {
            CreateRawResult("c1", visibility: "restricted", allowedGroups: ["team-a"]),
        };

        var (filtered, filteredOut) = AzureSearchRetrievalService.ApplyAclFilter(results, ["team-a"]);

        Assert.Single(filtered);
        Assert.Equal(0, filteredOut);
    }

    #endregion

    #region No-Evidence Detection

    [Fact]
    public void HasEvidence_True_WhenEnoughResultsAboveThreshold()
    {
        var settings = new RetrievalSettings
        {
            NoEvidenceScoreThreshold = 0.3f,
            NoEvidenceMinResults = 3,
        };

        var chunks = new List<RetrievedChunk>
        {
            CreateChunk("c1", rrfScore: 0.8),
            CreateChunk("c2", rrfScore: 0.5),
            CreateChunk("c3", rrfScore: 0.35),
            CreateChunk("c4", rrfScore: 0.1),
        };

        var aboveThreshold = chunks.Count(c => c.RrfScore >= settings.NoEvidenceScoreThreshold);
        var hasEvidence = aboveThreshold >= settings.NoEvidenceMinResults;

        Assert.True(hasEvidence);
        Assert.Equal(3, aboveThreshold);
    }

    [Fact]
    public void HasEvidence_False_WhenNotEnoughResultsAboveThreshold()
    {
        var settings = new RetrievalSettings
        {
            NoEvidenceScoreThreshold = 0.3f,
            NoEvidenceMinResults = 3,
        };

        var chunks = new List<RetrievedChunk>
        {
            CreateChunk("c1", rrfScore: 0.5),
            CreateChunk("c2", rrfScore: 0.4),
            CreateChunk("c3", rrfScore: 0.1),
            CreateChunk("c4", rrfScore: 0.05),
        };

        var aboveThreshold = chunks.Count(c => c.RrfScore >= settings.NoEvidenceScoreThreshold);
        var hasEvidence = aboveThreshold >= settings.NoEvidenceMinResults;

        Assert.False(hasEvidence);
        Assert.Equal(2, aboveThreshold);
    }

    [Fact]
    public void HasEvidence_False_WhenNoResults()
    {
        var settings = new RetrievalSettings();
        var chunks = new List<RetrievedChunk>();

        var aboveThreshold = chunks.Count(c => c.RrfScore >= settings.NoEvidenceScoreThreshold);
        var hasEvidence = aboveThreshold >= settings.NoEvidenceMinResults;

        Assert.False(hasEvidence);
    }

    [Fact]
    public void HasEvidence_ExactThreshold_CountsAsAbove()
    {
        var settings = new RetrievalSettings
        {
            NoEvidenceScoreThreshold = 0.3f,
            NoEvidenceMinResults = 1,
        };

        // Score exactly at the threshold boundary (using float-promoted-to-double precision).
        double threshold = settings.NoEvidenceScoreThreshold;
        var chunks = new List<RetrievedChunk>
        {
            CreateChunk("c1", rrfScore: threshold),
        };

        var aboveThreshold = chunks.Count(c => c.RrfScore >= threshold);
        var hasEvidence = aboveThreshold >= settings.NoEvidenceMinResults;

        Assert.True(hasEvidence);
    }

    #endregion

    #region OData Escaping

    [Fact]
    public void EscapeODataValue_EscapesSingleQuotes()
    {
        Assert.Equal("tenant''s", AzureSearchRetrievalService.EscapeODataValue("tenant's"));
    }

    [Fact]
    public void EscapeODataValue_NoChange_WhenNoQuotes()
    {
        Assert.Equal("tenant-123", AzureSearchRetrievalService.EscapeODataValue("tenant-123"));
    }

    #endregion

    #region RetrievalResult DTO

    [Fact]
    public void RetrievalResult_TraceId_Populated()
    {
        var result = new RetrievalResult
        {
            Chunks = [],
            AclFilteredOutCount = 0,
            HasEvidence = false,
            TraceId = "trace-123",
        };

        Assert.Equal("trace-123", result.TraceId);
    }

    [Fact]
    public void RetrievedChunk_SemanticScore_Nullable()
    {
        var chunk = CreateChunk("c1", rrfScore: 0.5);
        Assert.Null(chunk.SemanticScore);

        var chunkWithSemantic = chunk with { SemanticScore = 2.5 };
        Assert.Equal(2.5, chunkWithSemantic.SemanticScore);
    }

    #endregion

    #region Helpers

    private static RawSearchResult CreateRawResult(
        string chunkId,
        string visibility = "Internal",
        IReadOnlyList<string>? allowedGroups = null,
        double score = 0.5) => new()
    {
        ChunkId = chunkId,
        EvidenceId = $"ev-{chunkId}",
        ChunkText = $"Text for {chunkId}",
        Title = $"Title {chunkId}",
        SourceUrl = $"https://example.com/{chunkId}",
        SourceSystem = "AzureDevOps",
        SourceType = "WorkItem",
        UpdatedAt = DateTimeOffset.UtcNow,
        AccessLabel = visibility,
        Visibility = visibility,
        AllowedGroups = allowedGroups ?? [],
        Score = score,
    };

    private static RetrievedChunk CreateChunk(string chunkId, double rrfScore) => new()
    {
        ChunkId = chunkId,
        EvidenceId = $"ev-{chunkId}",
        ChunkText = $"Text for {chunkId}",
        Title = $"Title {chunkId}",
        SourceUrl = $"https://example.com/{chunkId}",
        SourceSystem = "AzureDevOps",
        SourceType = "WorkItem",
        UpdatedAt = DateTimeOffset.UtcNow,
        AccessLabel = "Internal",
        RrfScore = rrfScore,
    };

    #endregion
}
