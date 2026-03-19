using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

/// <summary>
/// Sends notifications when eval regressions or threshold violations are detected (P3-007).
/// </summary>
public interface IEvalNotificationService
{
    /// <summary>
    /// Sends a notification about eval regression/violations to configured webhook channels.
    /// Returns true if the notification was sent successfully (or no notification was needed).
    /// </summary>
    Task<bool> NotifyAsync(EvalNotificationPayload payload, CancellationToken ct = default);
}

/// <summary>
/// Payload for eval regression/violation notifications.
/// </summary>
public sealed record EvalNotificationPayload
{
    public required string RunId { get; init; }
    public required string RunType { get; init; }
    public required int TotalCases { get; init; }
    public required int SuccessfulCases { get; init; }
    public required int FailedCases { get; init; }
    public required bool HasBlockingRegression { get; init; }
    public required int ViolationCount { get; init; }
    public IReadOnlyList<EvalViolationDto>? Violations { get; init; }
    public EvalBaselineComparisonDto? BaselineComparison { get; init; }

    /// <summary>Optional link to the CI run or eval report for context.</summary>
    public string? RunUrl { get; init; }
}
