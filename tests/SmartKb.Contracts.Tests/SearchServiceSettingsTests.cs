using SmartKb.Contracts.Configuration;

namespace SmartKb.Contracts.Tests;

public class SearchServiceSettingsTests
{
    [Fact]
    public void IsConfigured_ReturnsFalse_WhenNoEndpoint()
    {
        var settings = new SearchServiceSettings();
        Assert.False(settings.IsConfigured);
    }

    [Fact]
    public void IsConfigured_ReturnsTrue_WhenEndpointSet()
    {
        var settings = new SearchServiceSettings { Endpoint = "https://srch-smartkb-dev.search.windows.net" };
        Assert.True(settings.IsConfigured);
        Assert.True(settings.UsesManagedIdentity);
    }

    [Fact]
    public void UsesManagedIdentity_ReturnsFalse_WhenApiKeySet()
    {
        var settings = new SearchServiceSettings
        {
            Endpoint = "https://srch-smartkb-dev.search.windows.net",
            AdminApiKey = "test-key",
        };
        Assert.True(settings.IsConfigured);
        Assert.False(settings.UsesManagedIdentity);
    }

    [Fact]
    public void DefaultIndexName_IsEvidence()
    {
        var settings = new SearchServiceSettings();
        Assert.Equal("evidence", settings.EvidenceIndexName);
    }

    [Fact]
    public void DefaultBatchSize_Is100()
    {
        var settings = new SearchServiceSettings();
        Assert.Equal(100, settings.IndexBatchSize);
    }

    [Fact]
    public void SectionName_IsSearchService()
    {
        Assert.Equal("SearchService", SearchServiceSettings.SectionName);
    }
}
