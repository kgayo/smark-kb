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

    [Fact]
    public void AdoApiVersion_IsCorrectValue()
    {
        Assert.Equal("7.1", ConnectorConstants.AdoApiVersion);
    }

    [Theory]
    [InlineData(nameof(ConnectorConstants.AdoFieldId), "System.Id")]
    [InlineData(nameof(ConnectorConstants.AdoFieldTitle), "System.Title")]
    [InlineData(nameof(ConnectorConstants.AdoFieldDescription), "System.Description")]
    [InlineData(nameof(ConnectorConstants.AdoFieldWorkItemType), "System.WorkItemType")]
    [InlineData(nameof(ConnectorConstants.AdoFieldState), "System.State")]
    [InlineData(nameof(ConnectorConstants.AdoFieldAreaPath), "System.AreaPath")]
    [InlineData(nameof(ConnectorConstants.AdoFieldAssignedTo), "System.AssignedTo")]
    [InlineData(nameof(ConnectorConstants.AdoFieldCreatedDate), "System.CreatedDate")]
    [InlineData(nameof(ConnectorConstants.AdoFieldChangedDate), "System.ChangedDate")]
    [InlineData(nameof(ConnectorConstants.AdoFieldTags), "System.Tags")]
    [InlineData(nameof(ConnectorConstants.AdoFieldTeamProject), "System.TeamProject")]
    public void AdoFieldName_HasExpectedValue(string fieldName, string expectedValue)
    {
        var field = typeof(ConnectorConstants).GetField(fieldName,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(field);
        Assert.Equal(expectedValue, field!.GetValue(null));
    }

    [Fact]
    public void AdoFieldNames_AllStartWithSystem()
    {
        var fields = typeof(ConnectorConstants)
            .GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            .Where(f => f.Name.StartsWith("AdoField"))
            .Select(f => (string)f.GetValue(null)!)
            .ToList();

        Assert.All(fields, f => Assert.StartsWith("System.", f));
        Assert.Equal(11, fields.Count);
    }
}
