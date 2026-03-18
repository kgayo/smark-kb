using SmartKb.Eval;
using SmartKb.Eval.Cli;

var datasetPath = GetArg("--dataset") ?? "eval/gold-dataset/baseline.jsonl";
var baselinePath = GetArg("--baseline") ?? "eval/gold-dataset/eval-baseline.json";
var outputPath = GetArg("--output") ?? "eval/eval-report.json";
var mode = GetArg("--mode")?.ToLowerInvariant() == "smoke" ? EvalMode.Smoke : EvalMode.Full;
var smokeCaseCount = int.TryParse(GetArg("--smoke-count"), out var sc) ? sc : 30;
var updateBaseline = HasFlag("--update-baseline");
var apiBaseUrl = GetArg("--api-url");
var apiToken = GetArg("--api-token");
var summaryPath = GetArg("--summary-path"); // GITHUB_STEP_SUMMARY

var settings = new EvalSettings();
var runner = new EvalCliRunner(settings);

HttpChatOrchestratorClient? orchestratorClient = null;
if (!string.IsNullOrEmpty(apiBaseUrl))
{
    orchestratorClient = new HttpChatOrchestratorClient(apiBaseUrl, apiToken);
}

var options = new EvalCliOptions
{
    DatasetPath = datasetPath,
    BaselinePath = baselinePath,
    OutputPath = outputPath,
    Mode = mode,
    SmokeCaseCount = smokeCaseCount,
    UpdateBaseline = updateBaseline,
    Orchestrator = orchestratorClient,
};

try
{
    var result = await runner.RunAsync(options, CancellationToken.None);

    // Emit GitHub Actions annotations to stdout
    Console.Write(result.Annotations);

    // Write job summary to GITHUB_STEP_SUMMARY or specified path
    var effectiveSummaryPath = summaryPath ?? Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
    if (!string.IsNullOrEmpty(effectiveSummaryPath))
    {
        await File.AppendAllTextAsync(effectiveSummaryPath, result.Summary);
    }

    // Also write summary to console for local runs
    Console.WriteLine(result.Summary);

    orchestratorClient?.Dispose();
    return result.ExitCode;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"::error title=Eval Fatal Error::{ex.Message}");
    Console.Error.WriteLine(ex.StackTrace);
    orchestratorClient?.Dispose();
    return 2;
}

string? GetArg(string name)
{
    var idx = Array.IndexOf(args, name);
    return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
}

bool HasFlag(string name) => args.Contains(name);
