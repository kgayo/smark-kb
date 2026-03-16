using SmartKb.Contracts.Configuration;

namespace SmartKb.Contracts.Tests;

public class EscalationSettingsTests
{
    [Theory]
    [InlineData("P1", "P2", true)]   // P1 >= P2
    [InlineData("P1", "P1", true)]   // P1 >= P1
    [InlineData("P2", "P2", true)]   // P2 >= P2
    [InlineData("P3", "P2", false)]  // P3 < P2
    [InlineData("P4", "P2", false)]  // P4 < P2
    [InlineData("P1", "P4", true)]   // P1 >= P4
    public void MeetsSeverityThreshold_ReturnsExpected(string severity, string minSeverity, bool expected)
    {
        Assert.Equal(expected, EscalationSettings.MeetsSeverityThreshold(severity, minSeverity));
    }

    [Theory]
    [InlineData("Critical", "P2")]
    [InlineData("P1", "High")]
    [InlineData("", "P2")]
    public void MeetsSeverityThreshold_ReturnsFalse_ForInvalidSeverity(string severity, string minSeverity)
    {
        Assert.False(EscalationSettings.MeetsSeverityThreshold(severity, minSeverity));
    }

    [Fact]
    public void Defaults_AreCorrect()
    {
        var settings = new EscalationSettings();
        Assert.Equal(0.4f, settings.DefaultConfidenceThreshold);
        Assert.Equal("P2", settings.DefaultMinSeverity);
        Assert.Equal("Engineering", settings.FallbackTargetTeam);
    }

    [Fact]
    public void SeverityOrder_HasFourLevels()
    {
        Assert.Equal(4, EscalationSettings.SeverityOrder.Length);
        Assert.Equal("P1", EscalationSettings.SeverityOrder[0]);
        Assert.Equal("P4", EscalationSettings.SeverityOrder[3]);
    }
}
