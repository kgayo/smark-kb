using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;

namespace SmartKb.Contracts.Tests;

public class FusedRetrievalServiceTests
{
    #region ACL Filtering

    [Fact]
    public void ApplyAclFilter_PublicDocuments_AlwaysPassThrough()
    {
        var results = new List<RankedResult>
        {
            CreateRanked("c1", visibility: "Public"),
            CreateRanked("c2", visibility: "Public"),
        };

        var (filtered, filteredOut) = FusedRetrievalService.ApplyAclFilter(results, null);

        Assert.Equal(2, filtered.Count);
        Assert.Equal(0, filteredOut);
    }

    [Fact]
    public void ApplyAclFilter_InternalDocuments_AlwaysPassThrough()
    {
        var results = new List<RankedResult>
        {
            CreateRanked("c1", visibility: "Internal"),
            CreateRanked("c2", visibility: "Internal"),
        };

        var (filtered, filteredOut) = FusedRetrievalService.ApplyAclFilter(results, null);

        Assert.Equal(2, filtered.Count);
        Assert.Equal(0, filteredOut);
    }

    [Fact]
    public void ApplyAclFilter_RestrictedDocuments_FilteredWhenNoUserGroups()
    {
        var results = new List<RankedResult>
        {
            CreateRanked("c1", visibility: "Restricted", allowedGroups: ["team-a"]),
        };

        var (filtered, filteredOut) = FusedRetrievalService.ApplyAclFilter(results, null);

        Assert.Empty(filtered);
        Assert.Equal(1, filteredOut);
    }

    [Fact]
    public void ApplyAclFilter_RestrictedDocuments_PassWhenUserInAllowedGroup()
    {
        var results = new List<RankedResult>
        {
            CreateRanked("c1", visibility: "Restricted", allowedGroups: ["team-a", "team-b"]),
        };

        var (filtered, filteredOut) = FusedRetrievalService.ApplyAclFilter(results, ["team-b"]);

        Assert.Single(filtered);
        Assert.Equal(0, filteredOut);
    }

    [Fact]
    public void ApplyAclFilter_MixedVisibility_CorrectCounts()
    {
        var results = new List<RankedResult>
        {
            CreateRanked("c1", visibility: "Public", resultSource: "Evidence"),
            CreateRanked("c2", visibility: "Internal", resultSource: "Pattern"),
            CreateRanked("c3", visibility: "Restricted", allowedGroups: ["team-a"], resultSource: "Evidence"),
            CreateRanked("c4", visibility: "Restricted", allowedGroups: ["team-b"], resultSource: "Pattern"),
        };

        var (filtered, filteredOut) = FusedRetrievalService.ApplyAclFilter(results, ["team-a"]);

        Assert.Equal(3, filtered.Count);
        Assert.Equal(1, filteredOut);
        Assert.DoesNotContain(filtered, r => r.Id == "c4");
    }

    [Fact]
    public void ApplyAclFilter_CaseInsensitiveGroupMatching()
    {
        var results = new List<RankedResult>
        {
            CreateRanked("c1", visibility: "Restricted", allowedGroups: ["Team-A"]),
        };

        var (filtered, filteredOut) = FusedRetrievalService.ApplyAclFilter(results, ["team-a"]);

        Assert.Single(filtered);
        Assert.Equal(0, filteredOut);
    }

    #endregion

    #region Trust Level Boost

    [Fact]
    public void GetTrustBoost_ApprovedPattern_ReturnsApprovedBoost()
    {
        var service = CreateService();
        Assert.Equal(1.5f, service.GetTrustBoost("Approved"));
    }

    [Fact]
    public void GetTrustBoost_ReviewedPattern_ReturnsReviewedBoost()
    {
        var service = CreateService();
        Assert.Equal(1.2f, service.GetTrustBoost("Reviewed"));
    }

    [Fact]
    public void GetTrustBoost_DraftPattern_ReturnsDraftBoost()
    {
        var service = CreateService();
        Assert.Equal(0.8f, service.GetTrustBoost("Draft"));
    }

    [Fact]
    public void GetTrustBoost_DeprecatedPattern_ReturnsDeprecatedBoost()
    {
        var service = CreateService();
        Assert.Equal(0.3f, service.GetTrustBoost("Deprecated"));
    }

    [Fact]
    public void GetTrustBoost_NullTrustLevel_ReturnsNeutral()
    {
        var service = CreateService();
        Assert.Equal(1.0f, service.GetTrustBoost(null));
    }

    [Fact]
    public void GetTrustBoost_CaseInsensitive()
    {
        var service = CreateService();
        Assert.Equal(1.5f, service.GetTrustBoost("approved"));
        Assert.Equal(1.2f, service.GetTrustBoost("REVIEWED"));
    }

    #endregion

    #region Recency Boost

    [Fact]
    public void ApplyRecencyBoost_RecentResult_GetsBoost()
    {
        var service = CreateService();
        var now = DateTimeOffset.UtcNow;
        var updatedAt = now.AddDays(-7); // 7 days ago

        var boosted = service.ApplyRecencyBoost(1.0, updatedAt, now);

        Assert.Equal(1.2, boosted, 0.01);
    }

    [Fact]
    public void ApplyRecencyBoost_MidRangeResult_NeutralBoost()
    {
        var service = CreateService();
        var now = DateTimeOffset.UtcNow;
        var updatedAt = now.AddDays(-60); // 60 days ago

        var boosted = service.ApplyRecencyBoost(1.0, updatedAt, now);

        Assert.Equal(1.0, boosted, 0.01);
    }

    [Fact]
    public void ApplyRecencyBoost_OldResult_GetsPenalty()
    {
        var service = CreateService();
        var now = DateTimeOffset.UtcNow;
        var updatedAt = now.AddDays(-120); // 120 days ago

        var boosted = service.ApplyRecencyBoost(1.0, updatedAt, now);

        Assert.Equal(0.8, boosted, 0.01);
    }

    [Fact]
    public void ApplyRecencyBoost_ExactBoundary30Days_GetsBoost()
    {
        var service = CreateService();
        var now = DateTimeOffset.UtcNow;
        var updatedAt = now.AddDays(-30); // exactly 30 days

        var boosted = service.ApplyRecencyBoost(1.0, updatedAt, now);

        Assert.Equal(1.2, boosted, 0.01);
    }

    [Fact]
    public void ApplyRecencyBoost_Boundary31Days_NeutralBoost()
    {
        var service = CreateService();
        var now = DateTimeOffset.UtcNow;
        var updatedAt = now.AddDays(-31); // 31 days ago

        var boosted = service.ApplyRecencyBoost(1.0, updatedAt, now);

        Assert.Equal(1.0, boosted, 0.01);
    }

    [Fact]
    public void ApplyRecencyBoost_CustomThresholds_UsesSettings()
    {
        var settings = new RetrievalSettings
        {
            RecencyRecentDays = 7,
            RecencyOldDays = 30,
            RecencyBoostRecent = 1.5f,
            RecencyBoostOld = 0.5f,
        };
        var service = CreateService(settings);
        var now = DateTimeOffset.UtcNow;

        // 5 days ago → within custom "recent" threshold of 7 days
        Assert.Equal(1.5, service.ApplyRecencyBoost(1.0, now.AddDays(-5), now), 0.01);

        // 15 days ago → between 7 and 30 → neutral
        Assert.Equal(1.0, service.ApplyRecencyBoost(1.0, now.AddDays(-15), now), 0.01);

        // 45 days ago → beyond custom "old" threshold of 30 days
        Assert.Equal(0.5, service.ApplyRecencyBoost(1.0, now.AddDays(-45), now), 0.01);
    }

    #endregion

    #region Diversity Constraint

    [Fact]
    public void ApplyDiversityConstraint_UnderLimit_AllKept()
    {
        var results = new List<RankedResult>
        {
            CreateRanked("c1", sourceId: "ev-1"),
            CreateRanked("c2", sourceId: "ev-2"),
            CreateRanked("c3", sourceId: "ev-3"),
        };

        var diversified = FusedRetrievalService.ApplyDiversityConstraint(results, 3);

        Assert.Equal(3, diversified.Count);
    }

    [Fact]
    public void ApplyDiversityConstraint_OverLimit_ExcessTrimmed()
    {
        var results = new List<RankedResult>
        {
            CreateRanked("c1", sourceId: "ev-1"),
            CreateRanked("c2", sourceId: "ev-1"),
            CreateRanked("c3", sourceId: "ev-1"),
            CreateRanked("c4", sourceId: "ev-1"), // 4th from ev-1, should be trimmed
            CreateRanked("c5", sourceId: "ev-2"),
        };

        var diversified = FusedRetrievalService.ApplyDiversityConstraint(results, 3);

        Assert.Equal(4, diversified.Count);
        Assert.Contains(diversified, r => r.Id == "c1");
        Assert.Contains(diversified, r => r.Id == "c2");
        Assert.Contains(diversified, r => r.Id == "c3");
        Assert.DoesNotContain(diversified, r => r.Id == "c4");
        Assert.Contains(diversified, r => r.Id == "c5");
    }

    [Fact]
    public void ApplyDiversityConstraint_PatternAndEvidence_BothLimited()
    {
        var results = new List<RankedResult>
        {
            CreateRanked("e1", sourceId: "ev-1", resultSource: "Evidence"),
            CreateRanked("e2", sourceId: "ev-1", resultSource: "Evidence"),
            CreateRanked("e3", sourceId: "ev-1", resultSource: "Evidence"),
            CreateRanked("e4", sourceId: "ev-1", resultSource: "Evidence"), // trimmed
            CreateRanked("p1", sourceId: "pat-1", resultSource: "Pattern"),
            CreateRanked("p2", sourceId: "pat-1", resultSource: "Pattern"),
        };

        var diversified = FusedRetrievalService.ApplyDiversityConstraint(results, 2);

        Assert.Equal(4, diversified.Count);
        Assert.DoesNotContain(diversified, r => r.Id == "e3");
        Assert.DoesNotContain(diversified, r => r.Id == "e4");
    }

    [Fact]
    public void ApplyDiversityConstraint_CaseInsensitiveSourceId()
    {
        var results = new List<RankedResult>
        {
            CreateRanked("c1", sourceId: "EV-1"),
            CreateRanked("c2", sourceId: "ev-1"),
            CreateRanked("c3", sourceId: "Ev-1"),
        };

        var diversified = FusedRetrievalService.ApplyDiversityConstraint(results, 2);

        Assert.Equal(2, diversified.Count);
    }

    #endregion

    #region OData Escaping

    [Fact]
    public void EscapeODataValue_EscapesSingleQuotes()
    {
        Assert.Equal("tenant''s", FusedRetrievalService.EscapeODataValue("tenant's"));
    }

    [Fact]
    public void EscapeODataValue_NoChange_WhenNoQuotes()
    {
        Assert.Equal("tenant-123", FusedRetrievalService.EscapeODataValue("tenant-123"));
    }

    #endregion

    #region RetrievalSettings Defaults

    [Fact]
    public void RetrievalSettings_RecencyThresholdDefaults_MatchOriginalValues()
    {
        var settings = new RetrievalSettings();
        Assert.Equal(30, settings.RecencyRecentDays);
        Assert.Equal(90, settings.RecencyOldDays);
    }

    #endregion

    #region RankedResult to RetrievedChunk Mapping

    [Fact]
    public void ToRetrievedChunk_MapsAllFields()
    {
        var result = new RankedResult
        {
            Id = "pattern-123",
            SourceId = "pattern-123",
            ChunkText = "Problem statement\n\n## Resolution Steps\nStep 1",
            ChunkContext = "Symptoms: slow response",
            Title = "Auth Timeout Pattern",
            SourceUrl = "https://patterns.example.com/123",
            SourceSystem = "Pattern",
            SourceType = "CasePattern",
            UpdatedAt = new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero),
            ProductArea = "Auth",
            AccessLabel = "Internal",
            Tags = ["auth", "timeout"],
            Visibility = "Internal",
            AllowedGroups = [],
            Score = 0.85,
            SemanticScore = 2.1,
            ResultSource = "Pattern",
            TrustLevel = "Approved",
            BoostedScore = 1.326,
        };

        var chunk = result.ToRetrievedChunk();

        Assert.Equal("pattern-123", chunk.ChunkId);
        Assert.Equal("pattern-123", chunk.EvidenceId);
        Assert.Contains("Resolution Steps", chunk.ChunkText);
        Assert.Equal("Symptoms: slow response", chunk.ChunkContext);
        Assert.Equal("Pattern", chunk.ResultSource);
        Assert.Equal("Approved", chunk.TrustLevel);
        Assert.Equal(1.326, chunk.BoostedScore);
        Assert.Equal(0.85, chunk.RrfScore);
        Assert.Equal(2.1, chunk.SemanticScore);
    }

    [Fact]
    public void ToRetrievedChunk_EvidenceResult_DefaultSourceAndNullTrustLevel()
    {
        var result = CreateRanked("c1", resultSource: "Evidence");
        result.BoostedScore = 0.5;

        var chunk = result.ToRetrievedChunk();

        Assert.Equal("Evidence", chunk.ResultSource);
        Assert.Null(chunk.TrustLevel);
    }

    #endregion

    #region RetrievedChunk P1-004 Fields

    [Fact]
    public void RetrievedChunk_ResultSource_DefaultsToEvidence()
    {
        var chunk = CreateChunk("c1", rrfScore: 0.5);
        Assert.Equal("Evidence", chunk.ResultSource);
    }

    [Fact]
    public void RetrievedChunk_TrustLevel_NullForEvidence()
    {
        var chunk = CreateChunk("c1", rrfScore: 0.5);
        Assert.Null(chunk.TrustLevel);
    }

    [Fact]
    public void RetrievedChunk_BoostedScore_DefaultsToZero()
    {
        var chunk = CreateChunk("c1", rrfScore: 0.5);
        Assert.Equal(0.0, chunk.BoostedScore);
    }

    [Fact]
    public void RetrievalResult_PatternCount_DefaultsToZero()
    {
        var result = new RetrievalResult
        {
            Chunks = [],
            AclFilteredOutCount = 0,
            HasEvidence = false,
            TraceId = "test",
        };
        Assert.Equal(0, result.PatternCount);
    }

    #endregion

    #region Helpers

    private static FusedRetrievalService CreateService(RetrievalSettings? settings = null)
    {
        var s = settings ?? new RetrievalSettings();
        return new FusedRetrievalService(
            null!, // SearchIndexClient not needed for unit tests
            new SearchServiceSettings(),
            s,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<FusedRetrievalService>.Instance);
    }

    private static RankedResult CreateRanked(
        string id,
        string sourceId = "",
        string visibility = "Internal",
        IReadOnlyList<string>? allowedGroups = null,
        string resultSource = "Evidence",
        string? trustLevel = null,
        double score = 0.5) => new()
    {
        Id = id,
        SourceId = string.IsNullOrEmpty(sourceId) ? $"ev-{id}" : sourceId,
        ChunkText = $"Text for {id}",
        Title = $"Title {id}",
        SourceUrl = $"https://example.com/{id}",
        SourceSystem = resultSource == "Pattern" ? "Pattern" : "AzureDevOps",
        SourceType = resultSource == "Pattern" ? "CasePattern" : "WorkItem",
        UpdatedAt = DateTimeOffset.UtcNow,
        AccessLabel = visibility,
        Visibility = visibility,
        AllowedGroups = allowedGroups ?? [],
        Score = score,
        ResultSource = resultSource,
        TrustLevel = trustLevel,
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
        Visibility = "Internal",
        RrfScore = rrfScore,
    };

    #endregion
}
