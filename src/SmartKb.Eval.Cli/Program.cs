using SmartKb.Contracts.Configuration;
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
var notifyWebhookUrl = GetArg("--notify-webhook") ?? Environment.GetEnvironmentVariable("EVAL_NOTIFY_WEBHOOK_URL");
var notifyFormat = GetArg("--notify-format") ?? Environment.GetEnvironmentVariable("EVAL_NOTIFY_FORMAT") ?? "generic";
var runUrl = GetArg("--run-url") ?? Environment.GetEnvironmentVariable("EVAL_RUN_URL");

var settings = new EvalSettings();
var runner = new EvalCliRunner(settings);

using var orchestratorClient = !string.IsNullOrEmpty(apiBaseUrl)
    ? new HttpChatOrchestratorClient(apiBaseUrl, apiToken)
    : null;

using var notificationService = !string.IsNullOrEmpty(notifyWebhookUrl)
    ? new WebhookEvalNotificationService(new EvalNotificationSettings
    {
        WebhookUrl = notifyWebhookUrl,
        Format = notifyFormat,
    })
    : null;

var options = new EvalCliOptions
{
    DatasetPath = datasetPath,
    BaselinePath = baselinePath,
    OutputPath = outputPath,
    Mode = mode,
    SmokeCaseCount = smokeCaseCount,
    UpdateBaseline = updateBaseline,
    Orchestrator = orchestratorClient,
    NotificationService = notificationService,
    RunUrl = runUrl,
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

    // Log notification result
    if (result.NotificationSent == true)
        Console.WriteLine("::notice title=Eval Notification::Regression alert sent to webhook.");
    else if (result.NotificationSent == false)
        Console.Error.WriteLine("::warning title=Eval Notification::Failed to send regression alert to webhook.");

    return result.ExitCode;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("::warning title=Eval Cancelled::Evaluation was cancelled.");
    return 130;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"::error title=Eval Fatal Error::{ex.Message}");
    Console.Error.WriteLine(ex.StackTrace);
    return 2;
}

string? GetArg(string name)
{
    var idx = Array.IndexOf(args, name);
    return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
}

bool HasFlag(string name) => args.Contains(name);
