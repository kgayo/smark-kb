using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Services;

namespace SmartKb.Contracts.Tests;

public class BlobStorageSettingsTests
{
    [Fact]
    public void IsConfigured_ReturnsFalse_WhenNoConnectionInfo()
    {
        var settings = new BlobStorageSettings();
        Assert.False(settings.IsConfigured);
    }

    [Fact]
    public void IsConfigured_ReturnsTrue_WhenServiceUriSet()
    {
        var settings = new BlobStorageSettings { ServiceUri = "https://test.blob.core.windows.net" };
        Assert.True(settings.IsConfigured);
        Assert.True(settings.UsesManagedIdentity);
    }

    [Fact]
    public void IsConfigured_ReturnsTrue_WhenConnectionStringSet()
    {
        var settings = new BlobStorageSettings { ConnectionString = "UseDevelopmentStorage=true" };
        Assert.True(settings.IsConfigured);
        Assert.False(settings.UsesManagedIdentity);
    }

    [Fact]
    public void UsesManagedIdentity_PrefersServiceUri()
    {
        var settings = new BlobStorageSettings
        {
            ServiceUri = "https://test.blob.core.windows.net",
            ConnectionString = "UseDevelopmentStorage=true",
        };
        Assert.True(settings.UsesManagedIdentity);
    }

    [Fact]
    public void DefaultContainerName_IsRawContent()
    {
        var settings = new BlobStorageSettings();
        Assert.Equal("raw-content", settings.RawContentContainer);
    }

    [Fact]
    public void SectionName_IsBlobStorage()
    {
        Assert.Equal("BlobStorage", BlobStorageSettings.SectionName);
    }
}

public class BlobPathConventionTests
{
    [Fact]
    public void BuildBlobPath_ProducesCorrectPath()
    {
        var path = IBlobStorageService.BuildBlobPath("tenant-abc", "AzureDevOps", "ev-123");
        Assert.Equal("tenant-abc/AzureDevOps/ev-123/raw", path);
    }

    [Fact]
    public void BuildBlobPath_HandlesSpecialCharacters()
    {
        var path = IBlobStorageService.BuildBlobPath("tenant-1", "SharePoint", "ev_with_underscores");
        Assert.Equal("tenant-1/SharePoint/ev_with_underscores/raw", path);
    }

    [Theory]
    [InlineData("t1", "AzureDevOps", "e1", "t1/AzureDevOps/e1/raw")]
    [InlineData("org-xyz", "SharePoint", "sp-doc-42", "org-xyz/SharePoint/sp-doc-42/raw")]
    public void BuildBlobPath_VariousInputs(string tenantId, string connectorType, string evidenceId, string expected)
    {
        Assert.Equal(expected, IBlobStorageService.BuildBlobPath(tenantId, connectorType, evidenceId));
    }
}
