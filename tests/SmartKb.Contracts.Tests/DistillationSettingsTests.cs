using SmartKb.Contracts.Configuration;

namespace SmartKb.Contracts.Tests;

public class DistillationSettingsTests
{
    [Fact]
    public void Defaults_AreCorrect()
    {
        var settings = new DistillationSettings();

        Assert.Equal(1, settings.MinPositiveFeedback);
        Assert.Equal(100, settings.MaxCandidates);
        Assert.Equal(20, settings.MaxBatchSize);
        Assert.Equal(1, settings.MinCitedChunks);
        Assert.Equal(0.5f, settings.BaseConfidence);
        Assert.Equal(0.05f, settings.PositiveFeedbackBoost);
        Assert.Equal(0.1f, settings.NegativeFeedbackPenalty);
        Assert.Equal(0.9f, settings.MaxConfidence);
    }

    [Fact]
    public void SolvedStatuses_DefaultContainsClosed()
    {
        var settings = new DistillationSettings();
        Assert.Contains("Closed", settings.SolvedStatuses);
    }

    [Fact]
    public void SectionName_IsDistillation()
    {
        Assert.Equal("Distillation", DistillationSettings.SectionName);
    }

    [Fact]
    public void Settings_AreConfigurable()
    {
        var settings = new DistillationSettings
        {
            MinPositiveFeedback = 3,
            MaxCandidates = 50,
            MaxBatchSize = 10,
            BaseConfidence = 0.6f,
        };

        Assert.Equal(3, settings.MinPositiveFeedback);
        Assert.Equal(50, settings.MaxCandidates);
        Assert.Equal(10, settings.MaxBatchSize);
        Assert.Equal(0.6f, settings.BaseConfidence);
    }
}
