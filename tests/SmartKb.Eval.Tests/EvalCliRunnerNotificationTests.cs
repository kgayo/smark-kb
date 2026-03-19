using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Eval.Cli;
using SmartKb.Eval.Models;

namespace SmartKb.Eval.Tests;

public class EvalCliRunnerNotificationTests
{
    [Fact]
    public async Task SendNotificationAsync_WhenNull_ReturnsNull()
    {
        var report = CreateReport();
        var violations = new List<ThresholdViolation>();

        var result = await EvalCliRunner.SendNotificationAsync(
            null, report, violations, null, null, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task SendNotificationAsync_OnSuccess_ReturnsTrue()
    {
        var fake = new FakeNotificationService(success: true);
        var report = CreateReport();
        var violations = new List<ThresholdViolation>
        {
            new() { MetricName = "Groundedness", ActualValue = 0.50f, ThresholdValue = 0.80f, Direction = ">=" },
        };

        var result = await EvalCliRunner.SendNotificationAsync(
            fake, report, violations, null, "https://ci.example.com/run/1", CancellationToken.None);

        Assert.True(result);
        Assert.NotNull(fake.LastPayload);
        Assert.Equal("eval-test-run", fake.LastPayload.RunId);
        Assert.Equal(1, fake.LastPayload.ViolationCount);
        Assert.Equal("https://ci.example.com/run/1", fake.LastPayload.RunUrl);
    }

    [Fact]
    public async Task SendNotificationAsync_OnFailure_ReturnsFalse()
    {
        var fake = new FakeNotificationService(success: false);
        var report = CreateReport();
        var violations = new List<ThresholdViolation>
        {
            new() { MetricName = "Groundedness", ActualValue = 0.50f, ThresholdValue = 0.80f, Direction = ">=" },
        };

        var result = await EvalCliRunner.SendNotificationAsync(
            fake, report, violations, null, null, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task SendNotificationAsync_OnException_ReturnsFalse()
    {
        var fake = new ThrowingNotificationService();
        var report = CreateReport();
        var violations = new List<ThresholdViolation>();

        var result = await EvalCliRunner.SendNotificationAsync(
            fake, report, violations, null, null, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task SendNotificationAsync_WithRegression_MapsBlockingFlag()
    {
        var fake = new FakeNotificationService(success: true);
        var report = CreateReport();
        var violations = new List<ThresholdViolation>();
        var regression = new RegressionResult
        {
            HasRegression = true,
            ShouldBlock = true,
            Details = new List<RegressionDetail>
            {
                new() { MetricName = "Groundedness", BaselineValue = 0.90f, CurrentValue = 0.70f, Delta = 0.20f, Severity = "blocking" },
            },
        };

        var result = await EvalCliRunner.SendNotificationAsync(
            fake, report, violations, regression, null, CancellationToken.None);

        Assert.True(result);
        Assert.NotNull(fake.LastPayload);
        Assert.True(fake.LastPayload.HasBlockingRegression);
        Assert.NotNull(fake.LastPayload.BaselineComparison);
        Assert.True(fake.LastPayload.BaselineComparison.ShouldBlock);
    }

    [Fact]
    public async Task SendNotificationAsync_MapsRunType_BasedOnCaseCount()
    {
        var fake = new FakeNotificationService(success: true);

        // 35 results => "full" (> 30)
        var report = CreateReport(caseCount: 35);
        var violations = new List<ThresholdViolation>
        {
            new() { MetricName = "Groundedness", ActualValue = 0.50f, ThresholdValue = 0.80f, Direction = ">=" },
        };

        await EvalCliRunner.SendNotificationAsync(
            fake, report, violations, null, null, CancellationToken.None);

        Assert.Equal("full", fake.LastPayload?.RunType);

        // 20 results => "smoke" (<= 30)
        var smallReport = CreateReport(caseCount: 20);
        await EvalCliRunner.SendNotificationAsync(
            fake, smallReport, violations, null, null, CancellationToken.None);

        Assert.Equal("smoke", fake.LastPayload?.RunType);
    }

    private static EvalReport CreateReport(int caseCount = 35) => new()
    {
        Timestamp = DateTimeOffset.UtcNow,
        RunId = "eval-test-run",
        TotalCases = caseCount,
        SuccessfulCases = caseCount - 2,
        FailedCases = 2,
        Metrics = new AggregateMetrics
        {
            Groundedness = 0.85f,
            CitationCoverage = 0.75f,
            RoutingAccuracy = 0.65f,
            NoEvidenceRate = 0.15f,
        },
        Results = Enumerable.Range(1, caseCount).Select(i => new EvalResult
        {
            CaseId = $"eval-{i:D5}",
            DurationMs = 1000,
            Response = new ChatResponse
            {
                ResponseType = "final_answer",
                Answer = "Test answer",
                Citations = [],
                Confidence = 0.8f,
                ConfidenceLabel = "High",
                TraceId = $"trace-{i}",
                HasEvidence = true,
                SystemPromptVersion = "test-v1",
            },
            Metrics = new CaseMetrics(),
        }).ToList(),
    };

    private sealed class FakeNotificationService : IEvalNotificationService
    {
        private readonly bool _success;
        public EvalNotificationPayload? LastPayload { get; private set; }

        public FakeNotificationService(bool success) => _success = success;

        public Task<bool> NotifyAsync(EvalNotificationPayload payload, CancellationToken ct = default)
        {
            LastPayload = payload;
            return Task.FromResult(_success);
        }
    }

    private sealed class ThrowingNotificationService : IEvalNotificationService
    {
        public Task<bool> NotifyAsync(EvalNotificationPayload payload, CancellationToken ct = default)
        {
            throw new InvalidOperationException("Webhook error");
        }
    }
}
