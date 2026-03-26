using SmartKb.Contracts.Connectors;

namespace SmartKb.Contracts.Tests;

public sealed class ConnectorConstantsTests
{
    [Fact]
    public void ClickUpTaskDeepLinkTemplate_ProducesCorrectUrl()
    {
        var result = string.Format(ConnectorConstants.ClickUpTaskDeepLinkTemplate, "abc123");
        Assert.Equal("https://app.clickup.com/t/abc123", result);
    }

    [Fact]
    public void HubSpotObjectDeepLinkTemplate_ProducesCorrectUrl()
    {
        var result = string.Format(ConnectorConstants.HubSpotObjectDeepLinkTemplate, "12345", "ticket", "67890");
        Assert.Equal("https://app.hubspot.com/contacts/12345/ticket/67890", result);
    }

    [Fact]
    public void JsonPatchMediaType_IsCorrectValue()
    {
        Assert.Equal("application/json-patch+json", ConnectorConstants.JsonPatchMediaType);
    }
}
