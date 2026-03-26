using Microsoft.Extensions.Logging;
using SmartKb.Contracts;
using SmartKb.Eval.Models;

namespace SmartKb.Eval.Tests;

public class BaselineComparatorTests
{
    private static readonly EvalSettings DefaultSettings = new();

    [Fact]
    public void Compare_NoRegression_AllOk()
    {
        var baseline = MakeMetrics(0.85f, 0.75f, 0.70f, 0.10f);
        var current = MakeMetrics(0.85f, 0.75f, 0.70f, 0.10f);

        var result = BaselineComparator.Compare(current, baseline, DefaultSettings);

        Assert.False(result.HasRegression);
        Assert.False(result.ShouldBlock);
        Assert.All(result.Details, d => Assert.Equal(EvalSeverity.Ok, d.Severity));
    }

    [Fact]
    public void Compare_SmallRegression_Warning()
    {
        var baseline = MakeMetrics(0.85f, 0.75f, 0.70f, 0.10f);
        var current = MakeMetrics(0.82f, 0.75f, 0.70f, 0.10f); // 3% drop in groundedness

        var result = BaselineComparator.Compare(current, baseline, DefaultSettings);

        Assert.True(result.HasRegression);
        Assert.False(result.ShouldBlock);

        var groundedness = result.Details.First(d => d.MetricName == "Groundedness");
        Assert.Equal(EvalSeverity.Warning, groundedness.Severity);
    }

    [Fact]
    public void Compare_LargeRegression_Blocking()
    {
        var baseline = MakeMetrics(0.85f, 0.75f, 0.70f, 0.10f);
        var current = MakeMetrics(0.79f, 0.75f, 0.70f, 0.10f); // 6% drop in groundedness

        var result = BaselineComparator.Compare(current, baseline, DefaultSettings);

        Assert.True(result.HasRegression);
        Assert.True(result.ShouldBlock);

        var groundedness = result.Details.First(d => d.MetricName == "Groundedness");
        Assert.Equal(EvalSeverity.Blocking, groundedness.Severity);
    }

    [Fact]
    public void Compare_NoEvidenceRateIncrease_Warning()
    {
        var baseline = MakeMetrics(noEvidenceRate: 0.10f);
        var current = MakeMetrics(noEvidenceRate: 0.13f); // 3% increase (lower is better)

        var result = BaselineComparator.Compare(current, baseline, DefaultSettings);

        Assert.True(result.HasRegression);
        var noEvidence = result.Details.First(d => d.MetricName == "NoEvidenceRate");
        Assert.Equal(EvalSeverity.Warning, noEvidence.Severity);
    }

    [Fact]
    public void Compare_NoEvidenceRateIncrease_Blocking()
    {
        var baseline = MakeMetrics(noEvidenceRate: 0.10f);
        var current = MakeMetrics(noEvidenceRate: 0.16f); // 6% increase

        var result = BaselineComparator.Compare(current, baseline, DefaultSettings);

        Assert.True(result.ShouldBlock);
        var noEvidence = result.Details.First(d => d.MetricName == "NoEvidenceRate");
        Assert.Equal(EvalSeverity.Blocking, noEvidence.Severity);
    }

    [Fact]
    public void Compare_Improvement_NoRegression()
    {
        var baseline = MakeMetrics(0.80f, 0.70f, 0.60f, 0.20f);
        var current = MakeMetrics(0.90f, 0.80f, 0.75f, 0.10f);

        var result = BaselineComparator.Compare(current, baseline, DefaultSettings);

        Assert.False(result.HasRegression);
        Assert.False(result.ShouldBlock);
    }

    [Fact]
    public void Compare_MultipleRegressions_ReportsAll()
    {
        var baseline = MakeMetrics(0.85f, 0.80f, 0.70f, 0.10f);
        var current = MakeMetrics(0.79f, 0.73f, 0.70f, 0.10f); // Two regressions

        var result = BaselineComparator.Compare(current, baseline, DefaultSettings);

        var regressions = result.Details.Where(d => d.Severity != EvalSeverity.Ok).ToList();
        Assert.Equal(2, regressions.Count);
    }

    [Fact]
    public void SerializeDeserialize_Baseline_RoundTrips()
    {
        var baseline = new EvalBaseline
        {
            Timestamp = DateTimeOffset.Parse("2026-03-17T00:00:00Z"),
            RunId = "test-run",
            TotalCases = 30,
            Metrics = MakeMetrics(0.85f, 0.75f, 0.70f, 0.10f),
        };

        var json = BaselineComparator.SerializeBaseline(baseline);
        var deserialized = BaselineComparator.DeserializeBaseline(json);

        Assert.NotNull(deserialized);
        Assert.Equal(baseline.RunId, deserialized!.RunId);
        Assert.Equal(baseline.TotalCases, deserialized.TotalCases);
        Assert.Equal(baseline.Metrics.Groundedness, deserialized.Metrics.Groundedness, 3);
    }

    [Fact]
    public void Compare_CustomThresholds_Applied()
    {
        var settings = new EvalSettings
        {
            RegressionWarningThreshold = 0.01f,
            RegressionBlockingThreshold = 0.03f,
        };

        var baseline = MakeMetrics(0.85f);
        var current = MakeMetrics(0.83f); // 2% drop — would be "warning" with defaults, "warning" with custom

        var result = BaselineComparator.Compare(current, baseline, settings);

        var groundedness = result.Details.First(d => d.MetricName == "Groundedness");
        Assert.Equal(EvalSeverity.Warning, groundedness.Severity);
    }

    [Fact]
    public void Compare_ExactlyAtWarningThreshold_IsWarning()
    {
        var baseline = MakeMetrics(0.85f);
        var current = MakeMetrics(0.83f); // Exactly 2% drop = warning threshold

        var result = BaselineComparator.Compare(current, baseline, DefaultSettings);

        var groundedness = result.Details.First(d => d.MetricName == "Groundedness");
        Assert.Equal(EvalSeverity.Warning, groundedness.Severity);
    }

    [Fact]
    public void DeserializeBaseline_ReturnsNull_ForMalformedJson()
    {
        var result = BaselineComparator.DeserializeBaseline("{invalid json");
        Assert.Null(result);
    }

    [Fact]
    public void DeserializeBaseline_MalformedJson_LogsWarningWhenLoggerProvided()
    {
        var logger = new CapturingLogger();

        var result = BaselineComparator.DeserializeBaseline("{invalid json", logger);

        Assert.Null(result);
        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, logger.Entries[0].Level);
        Assert.Contains("deserialize baseline", logger.Entries[0].Message);
    }

    [Fact]
    public async Task LoadBaselineAsync_MalformedJsonFile_LogsWarningWhenLoggerProvided()
    {
        var logger = new CapturingLogger();
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "{not valid json!!!");

            var result = await BaselineComparator.LoadBaselineAsync(tempFile, logger: logger);

            Assert.Null(result);
            Assert.Single(logger.Entries);
            Assert.Equal(LogLevel.Warning, logger.Entries[0].Level);
            Assert.Contains(tempFile, logger.Entries[0].Message);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task LoadBaselineAsync_MalformedJsonFile_ReturnsNullWithoutLogger()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "{not valid json!!!");

            var result = await BaselineComparator.LoadBaselineAsync(tempFile);

            Assert.Null(result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }

    private static AggregateMetrics MakeMetrics(
        float groundedness = 0.85f,
        float citationCoverage = 0.75f,
        float routingAccuracy = 0.70f,
        float noEvidenceRate = 0.10f,
        float responseTypeAccuracy = 0.90f,
        float mustIncludeHitRate = 0.85f,
        float safetyPassRate = 1f)
    {
        return new AggregateMetrics
        {
            Groundedness = groundedness,
            CitationCoverage = citationCoverage,
            RoutingAccuracy = routingAccuracy,
            NoEvidenceRate = noEvidenceRate,
            ResponseTypeAccuracy = responseTypeAccuracy,
            MustIncludeHitRate = mustIncludeHitRate,
            SafetyPassRate = safetyPassRate,
            AverageConfidence = 0.8f,
            AverageDurationMs = 500,
        };
    }
}
