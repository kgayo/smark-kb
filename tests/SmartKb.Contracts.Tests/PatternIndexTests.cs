using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Search;
using SmartKb.Contracts.Services;

namespace SmartKb.Contracts.Tests;

public class PatternIndexTests
{
    #region PatternFieldNames

    [Fact]
    public void PatternFieldNames_PatternId_EqualsExpected()
    {
        Assert.Equal("pattern_id", PatternFieldNames.PatternId);
    }

    [Fact]
    public void PatternFieldNames_EmbeddingDimensions_Matches1536()
    {
        Assert.Equal(1536, PatternFieldNames.EmbeddingDimensions);
        Assert.Equal(SearchFieldNames.EmbeddingDimensions, PatternFieldNames.EmbeddingDimensions);
    }

    [Fact]
    public void PatternFieldNames_VectorProfile_NotSameAsEvidence()
    {
        Assert.NotEqual(SearchFieldNames.VectorProfileName, PatternFieldNames.VectorProfileName);
    }

    [Fact]
    public void PatternFieldNames_SemanticConfig_NotSameAsEvidence()
    {
        Assert.NotEqual(SearchFieldNames.SemanticConfigName, PatternFieldNames.SemanticConfigName);
    }

    [Fact]
    public void PatternFieldNames_AllFieldsNonEmpty()
    {
        Assert.False(string.IsNullOrEmpty(PatternFieldNames.PatternId));
        Assert.False(string.IsNullOrEmpty(PatternFieldNames.Title));
        Assert.False(string.IsNullOrEmpty(PatternFieldNames.ProblemStatement));
        Assert.False(string.IsNullOrEmpty(PatternFieldNames.RootCause));
        Assert.False(string.IsNullOrEmpty(PatternFieldNames.Symptoms));
        Assert.False(string.IsNullOrEmpty(PatternFieldNames.ResolutionSteps));
        Assert.False(string.IsNullOrEmpty(PatternFieldNames.EmbeddingVector));
        Assert.False(string.IsNullOrEmpty(PatternFieldNames.TenantId));
        Assert.False(string.IsNullOrEmpty(PatternFieldNames.TrustLevel));
        Assert.False(string.IsNullOrEmpty(PatternFieldNames.ProductArea));
        Assert.False(string.IsNullOrEmpty(PatternFieldNames.Visibility));
        Assert.False(string.IsNullOrEmpty(PatternFieldNames.AllowedGroups));
        Assert.False(string.IsNullOrEmpty(PatternFieldNames.AccessLabel));
    }

    #endregion

    #region RetrievalSettings Fusion Defaults

    [Fact]
    public void RetrievalSettings_EnablePatternFusion_DefaultTrue()
    {
        var settings = new RetrievalSettings();
        Assert.True(settings.EnablePatternFusion);
    }

    [Fact]
    public void RetrievalSettings_PatternTopK_Default5()
    {
        var settings = new RetrievalSettings();
        Assert.Equal(5, settings.PatternTopK);
    }

    [Fact]
    public void RetrievalSettings_TrustBoostApproved_Default1Point5()
    {
        var settings = new RetrievalSettings();
        Assert.Equal(1.5f, settings.TrustBoostApproved);
    }

    [Fact]
    public void RetrievalSettings_TrustBoostDeprecated_Default0Point3()
    {
        var settings = new RetrievalSettings();
        Assert.Equal(0.3f, settings.TrustBoostDeprecated);
    }

    [Fact]
    public void RetrievalSettings_RecencyBoosts_Defaults()
    {
        var settings = new RetrievalSettings();
        Assert.Equal(1.2f, settings.RecencyBoostRecent);
        Assert.Equal(0.8f, settings.RecencyBoostOld);
    }

    [Fact]
    public void RetrievalSettings_PatternAuthorityBoost_Default1Point3()
    {
        var settings = new RetrievalSettings();
        Assert.Equal(1.3f, settings.PatternAuthorityBoost);
    }

    [Fact]
    public void RetrievalSettings_DiversityMaxPerSource_Default3()
    {
        var settings = new RetrievalSettings();
        Assert.Equal(3, settings.DiversityMaxPerSource);
    }

    #endregion

    #region SearchServiceSettings Pattern Index

    [Fact]
    public void SearchServiceSettings_PatternIndexName_DefaultPatterns()
    {
        var settings = new SearchServiceSettings();
        Assert.Equal("patterns", settings.PatternIndexName);
    }

    #endregion

    #region CasePattern Model

    [Fact]
    public void CasePattern_RequiredFields_SetCorrectly()
    {
        var pattern = new CasePattern
        {
            PatternId = "pattern-abc123",
            TenantId = "tenant-1",
            Title = "Auth Token Expiry",
            ProblemStatement = "Token expires during long operations",
            TrustLevel = TrustLevel.Approved,
            RelatedEvidenceIds = ["ev-001", "ev-002"],
        };

        Assert.Equal("pattern-abc123", pattern.PatternId);
        Assert.Equal("tenant-1", pattern.TenantId);
        Assert.Equal(TrustLevel.Approved, pattern.TrustLevel);
        Assert.Equal(2, pattern.RelatedEvidenceIds.Count);
    }

    [Fact]
    public void CasePattern_Defaults_Correct()
    {
        var pattern = new CasePattern
        {
            PatternId = "p1",
            TenantId = "t1",
            Title = "Test",
            ProblemStatement = "Problem",
            TrustLevel = TrustLevel.Draft,
            RelatedEvidenceIds = ["ev-1"],
        };

        Assert.Empty(pattern.Symptoms);
        Assert.Empty(pattern.DiagnosisSteps);
        Assert.Empty(pattern.ResolutionSteps);
        Assert.Empty(pattern.VerificationSteps);
        Assert.Empty(pattern.EscalationCriteria);
        Assert.Empty(pattern.ApplicabilityConstraints);
        Assert.Empty(pattern.Exclusions);
        Assert.Empty(pattern.Tags);
        Assert.Empty(pattern.AllowedGroups);
        Assert.Null(pattern.RootCause);
        Assert.Null(pattern.Workaround);
        Assert.Null(pattern.EscalationTargetTeam);
        Assert.Null(pattern.SupersedesPatternId);
        Assert.Null(pattern.ProductArea);
        Assert.Null(pattern.EmbeddingVector);
        Assert.Equal(1, pattern.Version);
        Assert.Equal(0f, pattern.Confidence);
        Assert.Equal(AccessVisibility.Internal, pattern.Visibility);
        Assert.Equal("Internal", pattern.AccessLabel);
        Assert.Equal(string.Empty, pattern.SourceUrl);
    }

    [Fact]
    public void CasePattern_AllTrustLevels_Valid()
    {
        Assert.Equal(4, Enum.GetValues<TrustLevel>().Length);
        Assert.Contains(TrustLevel.Draft, Enum.GetValues<TrustLevel>());
        Assert.Contains(TrustLevel.Reviewed, Enum.GetValues<TrustLevel>());
        Assert.Contains(TrustLevel.Approved, Enum.GetValues<TrustLevel>());
        Assert.Contains(TrustLevel.Deprecated, Enum.GetValues<TrustLevel>());
    }

    #endregion

    #region Pattern Indexing Service - Document Mapping

    [Fact]
    public void ToSearchDocument_MapsAllFields()
    {
        var pattern = new CasePattern
        {
            PatternId = "pattern-001",
            TenantId = "tenant-1",
            Title = "Auth Timeout Fix",
            ProblemStatement = "Users experience timeout during OAuth flow",
            Symptoms = ["timeout error", "401 response"],
            ResolutionSteps = ["Step 1: Check token expiry", "Step 2: Refresh token"],
            TrustLevel = TrustLevel.Approved,
            RelatedEvidenceIds = ["ev-1"],
            ProductArea = "Auth",
            Tags = ["auth", "oauth"],
            Confidence = 0.85f,
            Version = 2,
            Visibility = AccessVisibility.Internal,
            AllowedGroups = [],
            AccessLabel = "Internal",
            SourceUrl = "https://patterns.example.com/001",
            UpdatedAt = new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero),
            EmbeddingVector = new float[1536],
        };

        var doc = AzureSearchPatternIndexingService.ToSearchDocument(pattern);

        Assert.Equal("pattern-001", doc[PatternFieldNames.PatternId]);
        Assert.Equal("Auth Timeout Fix", doc[PatternFieldNames.Title]);
        Assert.Equal("Users experience timeout during OAuth flow", doc[PatternFieldNames.ProblemStatement]);
        Assert.Equal(string.Empty, doc[PatternFieldNames.RootCause]); // null RootCause maps to empty
        Assert.Contains("timeout error", doc[PatternFieldNames.Symptoms] as string ?? "");
        Assert.Contains("Step 1", doc[PatternFieldNames.ResolutionSteps] as string ?? "");
        Assert.Equal("tenant-1", doc[PatternFieldNames.TenantId]);
        Assert.Equal("Approved", doc[PatternFieldNames.TrustLevel]);
        Assert.Equal("Auth", doc[PatternFieldNames.ProductArea]);
        Assert.Equal(0.85, (double)doc[PatternFieldNames.Confidence], 0.001);
        Assert.Equal(2, doc[PatternFieldNames.Version]);
        Assert.Equal("Internal", doc[PatternFieldNames.Visibility]);
        Assert.Equal("Internal", doc[PatternFieldNames.AccessLabel]);
        Assert.Equal("https://patterns.example.com/001", doc[PatternFieldNames.SourceUrl]);
    }

    [Fact]
    public void ToSearchDocument_SymptomsJoinedWithNewline()
    {
        var pattern = new CasePattern
        {
            PatternId = "p1",
            TenantId = "t1",
            Title = "Test",
            ProblemStatement = "Test problem",
            Symptoms = ["symptom-a", "symptom-b"],
            ResolutionSteps = ["step-1", "step-2"],
            TrustLevel = TrustLevel.Draft,
            RelatedEvidenceIds = ["ev-1"],
            EmbeddingVector = new float[1536],
        };

        var doc = AzureSearchPatternIndexingService.ToSearchDocument(pattern);

        var symptoms = doc[PatternFieldNames.Symptoms] as string;
        Assert.Equal("symptom-a\nsymptom-b", symptoms);

        var steps = doc[PatternFieldNames.ResolutionSteps] as string;
        Assert.Equal("step-1\nstep-2", steps);
    }

    [Fact]
    public void ToSearchDocument_MapsRootCause()
    {
        var pattern = new CasePattern
        {
            PatternId = "p1",
            TenantId = "t1",
            Title = "Test",
            ProblemStatement = "Problem",
            RootCause = "Misconfigured DNS resolver",
            TrustLevel = TrustLevel.Draft,
            RelatedEvidenceIds = ["ev-1"],
            EmbeddingVector = new float[1536],
        };

        var doc = AzureSearchPatternIndexingService.ToSearchDocument(pattern);

        Assert.Equal("Misconfigured DNS resolver", doc[PatternFieldNames.RootCause]);
    }

    #endregion

    #region Pattern Index Schema

    [Fact]
    public void BuildIndexDefinition_HasCorrectName()
    {
        var settings = new SearchServiceSettings { PatternIndexName = "test-patterns" };
        var service = new AzureSearchPatternIndexingService(
            null!,
            settings,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AzureSearchPatternIndexingService>.Instance);

        var index = service.BuildIndexDefinition();

        Assert.Equal("test-patterns", index.Name);
    }

    [Fact]
    public void BuildIndexDefinition_HasKeyField()
    {
        var service = CreatePatternIndexingService();
        var index = service.BuildIndexDefinition();

        var keyField = index.Fields.FirstOrDefault(f => f.Name == PatternFieldNames.PatternId);
        Assert.NotNull(keyField);
        Assert.True(keyField.IsKey);
    }

    [Fact]
    public void BuildIndexDefinition_HasVectorSearchProfile()
    {
        var service = CreatePatternIndexingService();
        var index = service.BuildIndexDefinition();

        Assert.NotNull(index.VectorSearch);
        Assert.Single(index.VectorSearch.Profiles);
        Assert.Equal(PatternFieldNames.VectorProfileName, index.VectorSearch.Profiles[0].Name);
    }

    [Fact]
    public void BuildIndexDefinition_HasSemanticConfiguration()
    {
        var service = CreatePatternIndexingService();
        var index = service.BuildIndexDefinition();

        Assert.NotNull(index.SemanticSearch);
        Assert.Single(index.SemanticSearch.Configurations);
        Assert.Equal(PatternFieldNames.SemanticConfigName, index.SemanticSearch.Configurations[0].Name);
        var contentFields = index.SemanticSearch.Configurations[0].PrioritizedFields.ContentFields;
        Assert.Contains(contentFields, f => f.FieldName == PatternFieldNames.RootCause);
    }

    [Fact]
    public void BuildIndexDefinition_HasTrustLevelField()
    {
        var service = CreatePatternIndexingService();
        var index = service.BuildIndexDefinition();

        var field = index.Fields.FirstOrDefault(f => f.Name == PatternFieldNames.TrustLevel);
        Assert.NotNull(field);
        Assert.True(field.IsFilterable);
        Assert.True(field.IsFacetable);
    }

    [Fact]
    public void BuildIndexDefinition_HasAclFields()
    {
        var service = CreatePatternIndexingService();
        var index = service.BuildIndexDefinition();

        var visibility = index.Fields.FirstOrDefault(f => f.Name == PatternFieldNames.Visibility);
        Assert.NotNull(visibility);
        Assert.True(visibility.IsFilterable);

        var allowedGroups = index.Fields.FirstOrDefault(f => f.Name == PatternFieldNames.AllowedGroups);
        Assert.NotNull(allowedGroups);
        Assert.True(allowedGroups.IsFilterable);
    }

    [Fact]
    public void BuildIndexDefinition_HasSearchableTextFields()
    {
        var service = CreatePatternIndexingService();
        var index = service.BuildIndexDefinition();

        var fieldNames = index.Fields.Select(f => f.Name).ToList();
        Assert.Contains(PatternFieldNames.Title, fieldNames);
        Assert.Contains(PatternFieldNames.ProblemStatement, fieldNames);
        Assert.Contains(PatternFieldNames.RootCause, fieldNames);
        Assert.Contains(PatternFieldNames.Symptoms, fieldNames);
        Assert.Contains(PatternFieldNames.ResolutionSteps, fieldNames);
    }

    #endregion

    #region Helpers

    private static AzureSearchPatternIndexingService CreatePatternIndexingService()
    {
        return new AzureSearchPatternIndexingService(
            null!,
            new SearchServiceSettings(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AzureSearchPatternIndexingService>.Instance);
    }

    #endregion
}
