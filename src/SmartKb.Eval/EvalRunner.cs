using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Eval.Models;

namespace SmartKb.Eval;

/// <summary>
/// Runs evaluation cases through a chat orchestrator and produces an evaluation report.
/// Supports both live orchestration (nightly/weekly CI) and pre-recorded results (offline analysis).
/// </summary>
public sealed class EvalRunner
{
    private readonly IChatOrchestrator _orchestrator;
    private readonly EvalSettings _settings;
    private readonly ILogger<EvalRunner> _logger;

    public EvalRunner(IChatOrchestrator orchestrator, EvalSettings settings, ILogger<EvalRunner>? logger = null)
    {
        _orchestrator = orchestrator;
        _settings = settings;
        _logger = logger ?? NullLogger<EvalRunner>.Instance;
    }

    /// <summary>
    /// Runs all eval cases through the orchestrator and produces a report with aggregate metrics.
    /// </summary>
    public async Task<EvalReport> RunAsync(
        IReadOnlyList<EvalCase> cases,
        CancellationToken cancellationToken = default)
    {
        var runId = $"eval-run-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}";
        var results = new List<EvalResult>();

        foreach (var evalCase in cases)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await RunCaseAsync(evalCase, cancellationToken);
            results.Add(result);
        }

        var aggregateMetrics = MetricCalculator.ComputeAggregateMetrics(results);

        return new EvalReport
        {
            Timestamp = DateTimeOffset.UtcNow,
            RunId = runId,
            TotalCases = cases.Count,
            SuccessfulCases = results.Count(r => r.Error is null),
            FailedCases = results.Count(r => r.Error is not null),
            Metrics = aggregateMetrics,
            Results = results,
        };
    }

    /// <summary>
    /// Produces a report from pre-recorded eval results (no live orchestrator calls).
    /// </summary>
    public static EvalReport BuildReportFromResults(
        IReadOnlyList<EvalCase> cases,
        IReadOnlyList<(string CaseId, ChatResponse Response, long DurationMs)> recorded)
    {
        var runId = $"eval-run-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-offline";
        var resultMap = recorded.ToDictionary(r => r.CaseId);
        var results = new List<EvalResult>();

        foreach (var evalCase in cases)
        {
            if (resultMap.TryGetValue(evalCase.Id, out var rec))
            {
                var metrics = MetricCalculator.ComputeCaseMetrics(evalCase, rec.Response);
                results.Add(new EvalResult
                {
                    CaseId = evalCase.Id,
                    Response = rec.Response,
                    Metrics = metrics,
                    DurationMs = rec.DurationMs,
                });
            }
            else
            {
                results.Add(new EvalResult
                {
                    CaseId = evalCase.Id,
                    Response = EmptyResponse(),
                    Metrics = new CaseMetrics(),
                    Error = $"No recorded result for case {evalCase.Id}",
                });
            }
        }

        var aggregateMetrics = MetricCalculator.ComputeAggregateMetrics(results);

        return new EvalReport
        {
            Timestamp = DateTimeOffset.UtcNow,
            RunId = runId,
            TotalCases = cases.Count,
            SuccessfulCases = results.Count(r => r.Error is null),
            FailedCases = results.Count(r => r.Error is not null),
            Metrics = aggregateMetrics,
            Results = results,
        };
    }

    private async Task<EvalResult> RunCaseAsync(EvalCase evalCase, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var request = new ChatRequest
            {
                Query = evalCase.Query,
                SessionHistory = evalCase.Context?.SessionHistory?
                    .Select(m => new ChatMessage { Role = m.Role, Content = m.Content })
                    .ToList() ?? [],
            };

            var response = await _orchestrator.OrchestrateAsync(
                evalCase.TenantId,
                "eval-runner",
                $"eval-{evalCase.Id}",
                request,
                cancellationToken);

            sw.Stop();

            var metrics = MetricCalculator.ComputeCaseMetrics(evalCase, response);

            return new EvalResult
            {
                CaseId = evalCase.Id,
                Response = response,
                Metrics = metrics,
                DurationMs = sw.ElapsedMilliseconds,
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            _logger.LogWarning(ex, "Evaluation case {CaseId} failed after {DurationMs}ms", evalCase.Id, sw.ElapsedMilliseconds);

            return new EvalResult
            {
                CaseId = evalCase.Id,
                Response = EmptyResponse(),
                Metrics = new CaseMetrics(),
                DurationMs = sw.ElapsedMilliseconds,
                Error = ex.Message,
            };
        }
    }

    private static ChatResponse EmptyResponse() => new()
    {
        ResponseType = "error",
        Answer = string.Empty,
        Citations = [],
        Confidence = 0f,
        ConfidenceLabel = "Low",
        TraceId = string.Empty,
        HasEvidence = false,
        SystemPromptVersion = string.Empty,
    };
}
