using SmartKb.Contracts.Configuration;

namespace SmartKb.Contracts.Tests;

public sealed class SloSettingsTests
{
    [Fact]
    public void Defaults_MatchSloTargets()
    {
        var settings = new SloSettings();
        Assert.Equal(8000, settings.AnswerLatencyP95TargetMs);
        Assert.Equal(99.5, settings.AvailabilityTargetPercent);
        Assert.Equal(15, settings.SyncLagP95TargetMinutes);
        Assert.Equal(0.25, settings.NoEvidenceRateThreshold);
        Assert.Equal(10, settings.DeadLetterDepthThreshold);
        Assert.Equal(5, settings.AlertEvaluationWindowMinutes);
        Assert.Equal(50, settings.PiiRedactionSpikeThreshold);
    }

    [Fact]
    public void SectionName_IsCorrect()
    {
        Assert.Equal("Slo", SloSettings.SectionName);
    }

    [Fact]
    public void CanOverrideAllProperties()
    {
        var settings = new SloSettings
        {
            AnswerLatencyP95TargetMs = 5000,
            AvailabilityTargetPercent = 99.9,
            SyncLagP95TargetMinutes = 10,
            NoEvidenceRateThreshold = 0.15,
            DeadLetterDepthThreshold = 5,
            AlertEvaluationWindowMinutes = 10,
            PiiRedactionSpikeThreshold = 100,
        };
        Assert.Equal(5000, settings.AnswerLatencyP95TargetMs);
        Assert.Equal(99.9, settings.AvailabilityTargetPercent);
        Assert.Equal(10, settings.SyncLagP95TargetMinutes);
        Assert.Equal(0.15, settings.NoEvidenceRateThreshold);
        Assert.Equal(5, settings.DeadLetterDepthThreshold);
        Assert.Equal(10, settings.AlertEvaluationWindowMinutes);
        Assert.Equal(100, settings.PiiRedactionSpikeThreshold);
    }
}
