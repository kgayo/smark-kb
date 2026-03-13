using SmartKb.Contracts.Enums;

namespace SmartKb.Contracts.Tests;

public class EnumTests
{
    [Fact]
    public void ConnectorType_HasExpectedValues()
    {
        var values = Enum.GetValues<ConnectorType>();
        Assert.Equal(4, values.Length);
        Assert.Contains(ConnectorType.AzureDevOps, values);
        Assert.Contains(ConnectorType.SharePoint, values);
        Assert.Contains(ConnectorType.HubSpot, values);
        Assert.Contains(ConnectorType.ClickUp, values);
    }

    [Fact]
    public void ConnectorStatus_HasExpectedValues()
    {
        var values = Enum.GetValues<ConnectorStatus>();
        Assert.Equal(2, values.Length);
        Assert.Contains(ConnectorStatus.Enabled, values);
        Assert.Contains(ConnectorStatus.Disabled, values);
    }

    [Fact]
    public void SyncRunStatus_HasExpectedValues()
    {
        var values = Enum.GetValues<SyncRunStatus>();
        Assert.Equal(4, values.Length);
        Assert.Contains(SyncRunStatus.Pending, values);
        Assert.Contains(SyncRunStatus.Running, values);
        Assert.Contains(SyncRunStatus.Completed, values);
        Assert.Contains(SyncRunStatus.Failed, values);
    }

    [Fact]
    public void MessageRole_HasExpectedValues()
    {
        var values = Enum.GetValues<MessageRole>();
        Assert.Equal(2, values.Length);
        Assert.Contains(MessageRole.User, values);
        Assert.Contains(MessageRole.Assistant, values);
    }

    [Fact]
    public void FeedbackType_HasExpectedValues()
    {
        var values = Enum.GetValues<FeedbackType>();
        Assert.Equal(2, values.Length);
        Assert.Contains(FeedbackType.ThumbsUp, values);
        Assert.Contains(FeedbackType.ThumbsDown, values);
    }

    [Fact]
    public void ResolutionType_HasExpectedValues()
    {
        var values = Enum.GetValues<ResolutionType>();
        Assert.Equal(3, values.Length);
        Assert.Contains(ResolutionType.ResolvedWithoutEscalation, values);
        Assert.Contains(ResolutionType.Escalated, values);
        Assert.Contains(ResolutionType.Rerouted, values);
    }
}
