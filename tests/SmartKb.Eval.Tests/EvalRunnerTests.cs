using Microsoft.Extensions.Logging;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Eval.Models;

namespace SmartKb.Eval.Tests;

public class EvalRunnerTests
{
    [Fact]
    public async Task RunAsync_OrchestratorThrows_LogsWarningWithCaseId()
    {
        var orchestrator = new ThrowingOrchestrator(new InvalidOperationException("Test orchestrator failure"));
        var logger = new CapturingLogger();
        var runner = new EvalRunner(orchestrator, new EvalSettings(), logger);
        var cases = new[] { MakeCase("eval-fail-001", "final_answer") };

        var report = await runner.RunAsync(cases);

        Assert.Equal(1, report.FailedCases);
        Assert.Equal("Test orchestrator failure", report.Results[0].Error);
        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, logger.Entries[0].Level);
        Assert.Contains("eval-fail-001", logger.Entries[0].Message);
    }

    [Fact]
    public async Task RunAsync_OrchestratorThrows_CapturesDurationMs()
    {
        var orchestrator = new ThrowingOrchestrator(new InvalidOperationException("Timeout"));
        var runner = new EvalRunner(orchestrator, new EvalSettings());
        var cases = new[] { MakeCase("eval-dur-001", "final_answer") };

        var report = await runner.RunAsync(cases);

        Assert.True(report.Results[0].DurationMs >= 0);
    }

    [Fact]
    public async Task RunAsync_OrchestratorThrowsOperationCanceled_Propagates()
    {
        var orchestrator = new ThrowingOrchestrator(new OperationCanceledException());
        var runner = new EvalRunner(orchestrator, new EvalSettings());
        var cases = new[] { MakeCase("eval-cancel-001", "final_answer") };

        await Assert.ThrowsAsync<OperationCanceledException>(() => runner.RunAsync(cases));
    }

    [Fact]
    public void BuildReportFromResults_MatchesCasesToRecordings()
    {
        var cases = new[]
        {
            MakeCase("eval-00001", "final_answer", mustInclude: new[] { "password" }),
            MakeCase("eval-00002", "next_steps_only"),
        };

        var recorded = new (string, ChatResponse, long)[]
        {
            ("eval-00001", MakeResponse("final_answer", "Reset your password via the link.", hasEvidence: true), 400),
            ("eval-00002", MakeResponse("next_steps_only", "Try these steps.", hasEvidence: true), 300),
        };

        var report = EvalRunner.BuildReportFromResults(cases, recorded);

        Assert.Equal(2, report.TotalCases);
        Assert.Equal(2, report.SuccessfulCases);
        Assert.Equal(0, report.FailedCases);
        Assert.Equal(2, report.Results.Count);
        Assert.Contains("offline", report.RunId);
    }

    [Fact]
    public void BuildReportFromResults_MissingRecording_MarksError()
    {
        var cases = new[]
        {
            MakeCase("eval-00001", "final_answer"),
            MakeCase("eval-00002", "next_steps_only"),
        };

        var recorded = new (string, ChatResponse, long)[]
        {
            ("eval-00001", MakeResponse("final_answer"), 400),
            // eval-00002 is missing
        };

        var report = EvalRunner.BuildReportFromResults(cases, recorded);

        Assert.Equal(2, report.TotalCases);
        Assert.Equal(1, report.SuccessfulCases);
        Assert.Equal(1, report.FailedCases);

        var missing = report.Results.First(r => r.CaseId == "eval-00002");
        Assert.NotNull(missing.Error);
        Assert.Contains("No recorded result", missing.Error);
    }

    [Fact]
    public void BuildReportFromResults_ComputesAggregateMetrics()
    {
        var cases = new[]
        {
            MakeCase("eval-00001", "final_answer", mustInclude: new[] { "reset" }, mustCiteSources: true),
            MakeCase("eval-00002", "final_answer", mustInclude: new[] { "billing" }, mustCiteSources: true),
        };

        var recorded = new (string, ChatResponse, long)[]
        {
            ("eval-00001", MakeResponse("final_answer", "Reset via link.", citations: new[] { MakeCitation() }, hasEvidence: true, confidence: 0.9f), 500),
            ("eval-00002", MakeResponse("final_answer", "Check billing page.", citations: new[] { MakeCitation() }, hasEvidence: true, confidence: 0.7f), 600),
        };

        var report = EvalRunner.BuildReportFromResults(cases, recorded);

        Assert.True(report.Metrics.ResponseTypeAccuracy > 0);
        Assert.Equal(0f, report.Metrics.NoEvidenceRate);
        Assert.Equal(550, report.Metrics.AverageDurationMs);
    }

    [Fact]
    public void BuildReportFromResults_GeneratesRunId()
    {
        var cases = new[] { MakeCase("eval-00001", "final_answer") };
        var recorded = new (string, ChatResponse, long)[]
        {
            ("eval-00001", MakeResponse("final_answer"), 100),
        };

        var report = EvalRunner.BuildReportFromResults(cases, recorded);

        Assert.StartsWith("eval-run-", report.RunId);
        Assert.NotEqual(default, report.Timestamp);
    }

    // --- Helpers ---

    private static EvalCase MakeCase(
        string id, string responseType,
        IReadOnlyList<string>? mustInclude = null,
        bool? mustCiteSources = null)
    {
        return new EvalCase
        {
            Id = id,
            TenantId = "eval-tenant",
            Query = "Test query",
            Expected = new EvalExpected
            {
                ResponseType = responseType,
                MustInclude = mustInclude,
                MustCiteSources = mustCiteSources,
            },
        };
    }

    private static ChatResponse MakeResponse(
        string responseType = "final_answer",
        string answer = "Test answer",
        CitationDto[]? citations = null,
        bool hasEvidence = true,
        float confidence = 0.8f)
    {
        return new ChatResponse
        {
            ResponseType = responseType,
            Answer = answer,
            Citations = citations ?? [],
            Confidence = confidence,
            ConfidenceLabel = confidence >= 0.7f ? "High" : "Medium",
            HasEvidence = hasEvidence,
            TraceId = "test-trace",
            SystemPromptVersion = "1.0",
        };
    }

    private static CitationDto MakeCitation() => new()
    {
        ChunkId = "chunk-1",
        EvidenceId = "evidence-1",
        Title = "Test",
        SourceUrl = "https://example.com",
        SourceSystem = "AzureDevOps",
        Snippet = "Test snippet",
        UpdatedAt = DateTimeOffset.UtcNow,
        AccessLabel = "Internal",
    };

    // --- Test doubles ---

    private sealed class ThrowingOrchestrator(Exception exception) : IChatOrchestrator
    {
        public Task<ChatResponse> OrchestrateAsync(
            string tenantId, string userId, string correlationId,
            ChatRequest request, CancellationToken cancellationToken = default)
            => throw exception;
    }

    private sealed class CapturingLogger : ILogger<EvalRunner>
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
}
