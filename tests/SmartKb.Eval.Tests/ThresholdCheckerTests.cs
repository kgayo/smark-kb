using SmartKb.Eval.Models;

namespace SmartKb.Eval.Tests;

public class ThresholdCheckerTests
{
    private static readonly EvalSettings DefaultSettings = new();

    [Fact]
    public void Check_AllAboveThresholds_NoViolations()
    {
        var metrics = new AggregateMetrics
        {
            Groundedness = 0.85f,
            CitationCoverage = 0.75f,
            RoutingAccuracy = 0.65f,
            NoEvidenceRate = 0.20f,
        };

        var violations = ThresholdChecker.Check(metrics, DefaultSettings);
        Assert.Empty(violations);
    }

    [Fact]
    public void Check_GroundednessBelowThreshold_Violation()
    {
        var metrics = new AggregateMetrics
        {
            Groundedness = 0.75f,
            CitationCoverage = 0.75f,
            RoutingAccuracy = 0.65f,
            NoEvidenceRate = 0.20f,
        };

        var violations = ThresholdChecker.Check(metrics, DefaultSettings);
        Assert.Single(violations);
        Assert.Equal("Groundedness", violations[0].MetricName);
        Assert.Equal(">=", violations[0].Direction);
    }

    [Fact]
    public void Check_NoEvidenceRateAboveThreshold_Violation()
    {
        var metrics = new AggregateMetrics
        {
            Groundedness = 0.85f,
            CitationCoverage = 0.75f,
            RoutingAccuracy = 0.65f,
            NoEvidenceRate = 0.30f,
        };

        var violations = ThresholdChecker.Check(metrics, DefaultSettings);
        Assert.Single(violations);
        Assert.Equal("NoEvidenceRate", violations[0].MetricName);
        Assert.Equal("<=", violations[0].Direction);
    }

    [Fact]
    public void Check_MultipleViolations_ReportsAll()
    {
        var metrics = new AggregateMetrics
        {
            Groundedness = 0.50f,
            CitationCoverage = 0.50f,
            RoutingAccuracy = 0.40f,
            NoEvidenceRate = 0.40f,
        };

        var violations = ThresholdChecker.Check(metrics, DefaultSettings);
        Assert.Equal(4, violations.Count);
    }

    [Fact]
    public void Check_ExactlyAtThreshold_NoViolation()
    {
        var metrics = new AggregateMetrics
        {
            Groundedness = 0.80f,
            CitationCoverage = 0.70f,
            RoutingAccuracy = 0.60f,
            NoEvidenceRate = 0.25f,
        };

        var violations = ThresholdChecker.Check(metrics, DefaultSettings);
        Assert.Empty(violations);
    }

    [Fact]
    public void Check_CustomThresholds_Applied()
    {
        var settings = new EvalSettings
        {
            GroundednessThreshold = 0.90f,
            CitationCoverageThreshold = 0.80f,
        };

        var metrics = new AggregateMetrics
        {
            Groundedness = 0.85f,
            CitationCoverage = 0.75f,
            RoutingAccuracy = 0.65f,
            NoEvidenceRate = 0.20f,
        };

        var violations = ThresholdChecker.Check(metrics, settings);
        Assert.Equal(2, violations.Count);
    }

    [Fact]
    public void ViolationToString_FormatsCorrectly()
    {
        var violation = new ThresholdViolation
        {
            MetricName = "Groundedness",
            ActualValue = 0.75f,
            ThresholdValue = 0.80f,
            Direction = ">=",
        };

        var str = violation.ToString();
        Assert.Contains("Groundedness", str);
        Assert.Contains("0.750", str);
        Assert.Contains("0.800", str);
    }

    [Fact]
    public void MeetsMinimumCaseCount_AboveMin_ReturnsTrue()
    {
        Assert.True(ThresholdChecker.MeetsMinimumCaseCount(30, DefaultSettings));
        Assert.True(ThresholdChecker.MeetsMinimumCaseCount(50, DefaultSettings));
    }

    [Fact]
    public void MeetsMinimumCaseCount_BelowMin_ReturnsFalse()
    {
        Assert.False(ThresholdChecker.MeetsMinimumCaseCount(29, DefaultSettings));
        Assert.False(ThresholdChecker.MeetsMinimumCaseCount(0, DefaultSettings));
    }
}
