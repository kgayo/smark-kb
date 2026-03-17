namespace SmartKb.Eval.Tests;

public class EvalSettingsTests
{
    [Fact]
    public void DefaultSettings_AreValid()
    {
        var settings = new EvalSettings();
        Assert.True(settings.IsValid);
    }

    [Fact]
    public void DefaultSettings_HaveExpectedThresholds()
    {
        var settings = new EvalSettings();
        Assert.Equal(0.80f, settings.GroundednessThreshold);
        Assert.Equal(0.70f, settings.CitationCoverageThreshold);
        Assert.Equal(0.60f, settings.RoutingAccuracyThreshold);
        Assert.Equal(0.25f, settings.MaxNoEvidenceRate);
        Assert.Equal(0.02f, settings.RegressionWarningThreshold);
        Assert.Equal(0.05f, settings.RegressionBlockingThreshold);
        Assert.Equal(30, settings.MinCasesForGatedRelease);
    }

    [Fact]
    public void Settings_WithNegativeThreshold_NotValid()
    {
        var settings = new EvalSettings { GroundednessThreshold = -0.1f };
        Assert.False(settings.IsValid);
    }

    [Fact]
    public void Settings_WithThresholdAboveOne_NotValid()
    {
        var settings = new EvalSettings { CitationCoverageThreshold = 1.5f };
        Assert.False(settings.IsValid);
    }

    [Fact]
    public void Settings_WithBlockingLessThanWarning_NotValid()
    {
        var settings = new EvalSettings
        {
            RegressionWarningThreshold = 0.05f,
            RegressionBlockingThreshold = 0.02f,
        };
        Assert.False(settings.IsValid);
    }

    [Fact]
    public void Settings_WithZeroMinCases_NotValid()
    {
        var settings = new EvalSettings { MinCasesForGatedRelease = 0 };
        Assert.False(settings.IsValid);
    }
}
