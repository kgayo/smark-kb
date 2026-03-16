using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;

namespace SmartKb.Contracts.Tests;

public class BaselineEnrichmentServiceTests
{
    private readonly BaselineEnrichmentService _sut = new();

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

    // --- Category Detection ---

    [Fact]
    public void Enrich_DetectsCategory_Bug()
    {
        var record = CreateRecord(textContent: "NullReferenceException thrown when saving bug report");
        var result = _sut.Enrich(record);
        Assert.Equal("bug", result.Category);
    }

    [Fact]
    public void Enrich_DetectsCategory_FeatureRequest()
    {
        var record = CreateRecord(textContent: "Feature request: add dark mode support");
        var result = _sut.Enrich(record);
        Assert.Equal("feature_request", result.Category);
    }

    [Fact]
    public void Enrich_DetectsCategory_Incident()
    {
        var record = CreateRecord(textContent: "Production outage affecting all users");
        var result = _sut.Enrich(record);
        Assert.Equal("incident", result.Category);
    }

    [Fact]
    public void Enrich_DetectsCategory_Question()
    {
        var record = CreateRecord(textContent: "How to configure the VPN settings?");
        var result = _sut.Enrich(record);
        Assert.Equal("question", result.Category);
    }

    [Fact]
    public void Enrich_DetectsCategory_Documentation_ForWikiPages()
    {
        var record = CreateRecord(sourceType: SourceType.WikiPage, textContent: "Setup guide content");
        var result = _sut.Enrich(record);
        Assert.Equal("documentation", result.Category);
    }

    [Fact]
    public void Enrich_Category_ReturnsNull_WhenNoMatch()
    {
        var record = CreateRecord(textContent: "Updated the configuration value to 42.");
        var result = _sut.Enrich(record);
        Assert.Null(result.Category);
    }

    // --- Severity Detection ---

    [Fact]
    public void Enrich_PreservesExistingSeverity()
    {
        var record = CreateRecord(severity: "high", textContent: "Some low priority thing");
        var result = _sut.Enrich(record);
        Assert.Equal("high", result.Severity);
    }

    [Fact]
    public void Enrich_DetectsSeverity_Critical()
    {
        var record = CreateRecord(textContent: "This is sev-1 critical issue");
        var result = _sut.Enrich(record);
        Assert.Equal("critical", result.Severity);
    }

    [Fact]
    public void Enrich_DetectsSeverity_Medium()
    {
        var record = CreateRecord(textContent: "Medium priority fix for P3 issues");
        var result = _sut.Enrich(record);
        Assert.Equal("medium", result.Severity);
    }

    // --- Environment Detection ---

    [Fact]
    public void Enrich_DetectsEnvironment_Production()
    {
        var record = CreateRecord(textContent: "Error occurred in production environment");
        var result = _sut.Enrich(record);
        Assert.Equal("production", result.Environment);
    }

    [Fact]
    public void Enrich_DetectsEnvironment_Staging()
    {
        var record = CreateRecord(textContent: "Issue found in staging environment during testing");
        var result = _sut.Enrich(record);
        Assert.Equal("staging", result.Environment);
    }

    [Fact]
    public void Enrich_Environment_ReturnsNull_WhenNoMatch()
    {
        var record = CreateRecord(textContent: "Updated a field.");
        var result = _sut.Enrich(record);
        Assert.Null(result.Environment);
    }

    // --- Error Token Extraction ---

    [Fact]
    public void ExtractErrorTokens_FindsExceptionNames()
    {
        var tokens = BaselineEnrichmentService.ExtractErrorTokens("Got NullReferenceException and IOException");
        Assert.Contains("NullReferenceException", tokens);
        Assert.Contains("IOException", tokens);
    }

    [Fact]
    public void ExtractErrorTokens_FindsHttpStatusCodes()
    {
        var tokens = BaselineEnrichmentService.ExtractErrorTokens("API returned HTTP 500 and status 404");
        Assert.Contains("HTTP 500", tokens);
        Assert.Contains("HTTP 404", tokens);
    }

    [Fact]
    public void ExtractErrorTokens_FindsErrorCodes()
    {
        var tokens = BaselineEnrichmentService.ExtractErrorTokens("Got error AADSTS50076 during auth");
        Assert.Contains("AADSTS50076", tokens);
    }

    [Fact]
    public void ExtractErrorTokens_FindsHexCodes()
    {
        var tokens = BaselineEnrichmentService.ExtractErrorTokens("Windows error 0x80070005 access denied");
        Assert.Contains("0x80070005", tokens);
    }

    [Fact]
    public void ExtractErrorTokens_ReturnsEmpty_WhenNoTokens()
    {
        var tokens = BaselineEnrichmentService.ExtractErrorTokens("Everything is working fine.");
        Assert.Empty(tokens);
    }

    [Fact]
    public void ExtractErrorTokens_DeduplicatesTokens()
    {
        var tokens = BaselineEnrichmentService.ExtractErrorTokens(
            "NullReferenceException happened. Another NullReferenceException later.");
        Assert.Single(tokens, t => t == "NullReferenceException");
    }

    // --- PII Detection ---

    [Fact]
    public void DetectPii_FindsEmail()
    {
        var flags = BaselineEnrichmentService.DetectPii("Contact user@example.com for help");
        Assert.Contains("email", flags);
    }

    [Fact]
    public void DetectPii_FindsSsn()
    {
        var flags = BaselineEnrichmentService.DetectPii("SSN: 123-45-6789 on file");
        Assert.Contains("ssn", flags);
    }

    [Fact]
    public void DetectPii_FindsCreditCard()
    {
        var flags = BaselineEnrichmentService.DetectPii("Card: 4111-1111-1111-1111");
        Assert.Contains("credit_card", flags);
    }

    [Fact]
    public void DetectPii_ReturnsEmpty_WhenNoPii()
    {
        var flags = BaselineEnrichmentService.DetectPii("No personal information here.");
        Assert.Empty(flags);
    }

    // --- Enrichment Version ---

    [Fact]
    public void Enrich_SetsEnrichmentVersion()
    {
        var record = CreateRecord();
        var result = _sut.Enrich(record);
        Assert.Equal(BaselineEnrichmentService.CurrentEnrichmentVersion, result.EnrichmentVersion);
    }

    // --- ProductArea Passthrough ---

    [Fact]
    public void Enrich_PreservesProductArea()
    {
        var record = CreateRecord(productArea: "Networking");
        var result = _sut.Enrich(record);
        Assert.Equal("Networking", result.ProductArea);
    }
}
