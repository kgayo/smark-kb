using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;

namespace SmartKb.Contracts.Tests;

public class EnhancedEnrichmentServiceTests
{
    private readonly EnhancedEnrichmentService _sut = new();

    private static CanonicalRecord CreateRecord(
        string title = "Test",
        string textContent = "Some content",
        SourceType sourceType = SourceType.WorkItem,
        string? severity = null,
        string? productArea = null)
    {
        return new CanonicalRecord
        {
            TenantId = "t1",
            EvidenceId = "ev-1",
            SourceSystem = ConnectorType.AzureDevOps,
            SourceType = sourceType,
            SourceLocator = new SourceLocator("1", "https://example.com"),
            Title = title,
            TextContent = textContent,
            ContentHash = "hash",
            AccessLabel = "Internal",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Status = EvidenceStatus.Open,
            Permissions = new RecordPermissions(AccessVisibility.Internal, []),
            Severity = severity,
            ProductArea = productArea,
        };
    }

    // --- Enrichment Version ---

    [Fact]
    public void Enrich_SetsVersion2()
    {
        var record = CreateRecord();
        var result = _sut.Enrich(record);
        Assert.Equal(2, result.EnrichmentVersion);
    }

    // --- Weighted Category Detection ---

    [Fact]
    public void Enrich_DetectsCategory_Bug_WithConfidence()
    {
        var record = CreateRecord(textContent: "NullReferenceException thrown, crash in production, stack trace shows bug");
        var result = _sut.Enrich(record);
        Assert.Equal("bug", result.Category);
        Assert.True(result.CategoryConfidence > 0f);
    }

    [Fact]
    public void Enrich_WeightedCategory_HigherConfidenceForMoreSignals()
    {
        var fewSignals = CreateRecord(textContent: "There is a bug in the system");
        var manySignals = CreateRecord(textContent: "Bug report: NullReferenceException crash with stack trace showing error failure");
        var resultFew = _sut.Enrich(fewSignals);
        var resultMany = _sut.Enrich(manySignals);
        Assert.Equal("bug", resultFew.Category);
        Assert.Equal("bug", resultMany.Category);
        Assert.True(resultMany.CategoryConfidence >= resultFew.CategoryConfidence);
    }

    [Fact]
    public void Enrich_WeightedCategory_PicksHighestScoringCategory()
    {
        // "bug" has just one signal, "incident" has multiple
        var record = CreateRecord(textContent: "Major incident outage causing downtime and service disruption, degradation affecting customer impact");
        var result = _sut.Enrich(record);
        Assert.Equal("incident", result.Category);
    }

    [Fact]
    public void Enrich_WeightedCategory_ReturnsNullForNoMatch()
    {
        var record = CreateRecord(textContent: "Updated configuration parameter to 42.");
        var result = _sut.Enrich(record);
        Assert.Null(result.Category);
        Assert.Equal(0f, result.CategoryConfidence);
    }

    [Fact]
    public void Enrich_WeightedCategory_WikiPageIsDocumentation()
    {
        var record = CreateRecord(sourceType: SourceType.WikiPage, textContent: "Setup guide");
        var result = _sut.Enrich(record);
        Assert.Equal("documentation", result.Category);
        Assert.Equal(1.0f, result.CategoryConfidence);
    }

    // --- Technology Tag Detection ---

    [Fact]
    public void Enrich_DetectsTechnologyTags_AzureServices()
    {
        var record = CreateRecord(textContent: "Issue with Azure SQL connection and service bus message processing");
        var result = _sut.Enrich(record);
        Assert.Contains("Azure SQL", result.TechnologyTags);
        Assert.Contains("Azure Service Bus", result.TechnologyTags);
    }

    [Fact]
    public void Enrich_DetectsTechnologyTags_DotNetStack()
    {
        var record = CreateRecord(textContent: "C# application using Entity Framework with DbContext errors");
        var result = _sut.Enrich(record);
        Assert.Contains(".NET", result.TechnologyTags);
        Assert.Contains("Entity Framework", result.TechnologyTags);
    }

    [Fact]
    public void Enrich_DetectsTechnologyTags_Frontend()
    {
        var record = CreateRecord(textContent: "React component not rendering, useEffect hook failing");
        var result = _sut.Enrich(record);
        Assert.Contains("React", result.TechnologyTags);
    }

    [Fact]
    public void Enrich_DetectsTechnologyTags_ReturnsEmptyForNoMatch()
    {
        var record = CreateRecord(textContent: "General system update completed.");
        var result = _sut.Enrich(record);
        Assert.Empty(result.TechnologyTags);
    }

    [Fact]
    public void Enrich_DetectsTechnologyTags_InfraTools()
    {
        var record = CreateRecord(textContent: "Terraform plan showing drift in kubernetes cluster");
        var result = _sut.Enrich(record);
        Assert.Contains("Terraform", result.TechnologyTags);
        Assert.Contains("Kubernetes", result.TechnologyTags);
    }

    // --- Component Extraction ---

    [Fact]
    public void Enrich_ExtractsComponent_ExplicitLabel()
    {
        var record = CreateRecord(textContent: "Component: Auth Service\nFailing to validate tokens");
        var result = _sut.Enrich(record);
        Assert.Equal("Auth Service", result.Component);
    }

    [Fact]
    public void Enrich_ExtractsComponent_ModuleLabel()
    {
        var record = CreateRecord(textContent: "Module: Ingestion Pipeline\nWebhook processing stalls");
        var result = _sut.Enrich(record);
        Assert.Equal("Ingestion Pipeline", result.Component);
    }

    [Fact]
    public void Enrich_ExtractsComponent_NamespaceStyle()
    {
        var record = CreateRecord(textContent: "Error in SmartKb.Api.Controllers class");
        var result = _sut.Enrich(record);
        Assert.Equal("SmartKb.Api.Controllers", result.Component);
    }

    [Fact]
    public void Enrich_ExtractsComponent_ReturnsNullWhenNone()
    {
        var record = CreateRecord(textContent: "Something is wrong with the system.");
        var result = _sut.Enrich(record);
        Assert.Null(result.Component);
    }

    // --- Product Area Inference ---

    [Fact]
    public void Enrich_PreservesExistingProductArea()
    {
        var record = CreateRecord(productArea: "Networking", textContent: "Auth token validation failure with SSO login");
        var result = _sut.Enrich(record);
        Assert.Equal("Networking", result.ProductArea);
    }

    [Fact]
    public void Enrich_InfersProductArea_Authentication()
    {
        var record = CreateRecord(textContent: "SSO login authentication failure with OAuth token issues");
        var result = _sut.Enrich(record);
        Assert.Equal("Authentication", result.ProductArea);
    }

    [Fact]
    public void Enrich_InfersProductArea_Database()
    {
        var record = CreateRecord(textContent: "SQL migration failed with deadlock on index rebuild, database schema issue");
        var result = _sut.Enrich(record);
        Assert.Equal("Database", result.ProductArea);
    }

    [Fact]
    public void Enrich_InfersProductArea_RequiresMultipleKeywords()
    {
        // Only one keyword match — not confident enough
        var record = CreateRecord(textContent: "Need to deploy the update.");
        var result = _sut.Enrich(record);
        Assert.Null(result.ProductArea);
    }

    // --- Baseline Passthrough ---

    [Fact]
    public void Enrich_PreservesSeverityFromBaseline()
    {
        var record = CreateRecord(severity: "high", textContent: "Some low priority thing");
        var result = _sut.Enrich(record);
        Assert.Equal("high", result.Severity);
    }

    [Fact]
    public void Enrich_PreservesErrorTokensFromBaseline()
    {
        var record = CreateRecord(textContent: "Got NullReferenceException and HTTP 500");
        var result = _sut.Enrich(record);
        Assert.Contains("NullReferenceException", result.ErrorTokens);
        Assert.Contains("HTTP 500", result.ErrorTokens);
    }

    [Fact]
    public void Enrich_PreservesPiiFromBaseline()
    {
        var record = CreateRecord(textContent: "Contact user@example.com for help");
        var result = _sut.Enrich(record);
        Assert.Contains("email", result.PiiFlags);
    }

    [Fact]
    public void Enrich_PreservesEnvironmentFromBaseline()
    {
        var record = CreateRecord(textContent: "Error in production environment causing outage");
        var result = _sut.Enrich(record);
        Assert.Equal("production", result.Environment);
    }
}
