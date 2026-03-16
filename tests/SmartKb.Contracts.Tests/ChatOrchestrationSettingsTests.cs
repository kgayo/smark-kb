using SmartKb.Contracts.Configuration;

namespace SmartKb.Contracts.Tests;

public class ChatOrchestrationSettingsTests
{
    [Fact]
    public void SectionName_IsChatOrchestration()
    {
        Assert.Equal("ChatOrchestration", ChatOrchestrationSettings.SectionName);
    }

    [Fact]
    public void Defaults_MaxTokenBudget_Is102400()
    {
        var settings = new ChatOrchestrationSettings();
        Assert.Equal(102_400, settings.MaxTokenBudget);
    }

    [Fact]
    public void Defaults_MaxResponseTokens_Is4096()
    {
        var settings = new ChatOrchestrationSettings();
        Assert.Equal(4096, settings.MaxResponseTokens);
    }

    [Fact]
    public void Defaults_ConfidenceWeights_SumToOne()
    {
        var settings = new ChatOrchestrationSettings();
        Assert.Equal(1.0f, settings.ModelConfidenceWeight + settings.RetrievalConfidenceWeight, 0.001f);
    }

    [Fact]
    public void Defaults_HighConfidenceThreshold_Is07()
    {
        var settings = new ChatOrchestrationSettings();
        Assert.Equal(0.7f, settings.HighConfidenceThreshold);
    }

    [Fact]
    public void Defaults_MediumConfidenceThreshold_Is04()
    {
        var settings = new ChatOrchestrationSettings();
        Assert.Equal(0.4f, settings.MediumConfidenceThreshold);
    }

    [Fact]
    public void Defaults_DegradationThreshold_Is03()
    {
        var settings = new ChatOrchestrationSettings();
        Assert.Equal(0.3f, settings.DegradationThreshold);
    }

    [Fact]
    public void GetConfidenceLabel_High_WhenAboveThreshold()
    {
        var settings = new ChatOrchestrationSettings();
        Assert.Equal("High", settings.GetConfidenceLabel(0.8f));
        Assert.Equal("High", settings.GetConfidenceLabel(0.7f));
    }

    [Fact]
    public void GetConfidenceLabel_Medium_WhenInRange()
    {
        var settings = new ChatOrchestrationSettings();
        Assert.Equal("Medium", settings.GetConfidenceLabel(0.5f));
        Assert.Equal("Medium", settings.GetConfidenceLabel(0.4f));
    }

    [Fact]
    public void GetConfidenceLabel_Low_WhenBelowMedium()
    {
        var settings = new ChatOrchestrationSettings();
        Assert.Equal("Low", settings.GetConfidenceLabel(0.3f));
        Assert.Equal("Low", settings.GetConfidenceLabel(0.0f));
    }

    [Fact]
    public void Defaults_MaxEvidenceChunksInPrompt_Is15()
    {
        var settings = new ChatOrchestrationSettings();
        Assert.Equal(15, settings.MaxEvidenceChunksInPrompt);
    }

    [Fact]
    public void Defaults_MaxCitations_Is10()
    {
        var settings = new ChatOrchestrationSettings();
        Assert.Equal(10, settings.MaxCitations);
    }

    [Fact]
    public void Defaults_SystemPromptVersion_Is10()
    {
        var settings = new ChatOrchestrationSettings();
        Assert.Equal("1.0", settings.SystemPromptVersion);
    }
}
