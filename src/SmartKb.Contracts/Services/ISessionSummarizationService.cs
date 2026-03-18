using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

/// <summary>
/// Summarizes session messages that are about to be dropped by the sliding window token budget (P3-002).
/// Produces a compact context block preserving key issue, attempted solutions, and unresolved questions
/// so that early conversation context is not lost in long multi-turn sessions.
/// Design decision D-016 resolved: use gpt-4o-mini for cost efficiency, 200-token structured summary.
/// </summary>
public interface ISessionSummarizationService
{
    /// <summary>
    /// Summarizes the given messages into a compact context block.
    /// Called before the sliding window drops oldest messages.
    /// </summary>
    /// <param name="messagesToSummarize">Messages about to be dropped from the session window.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A structured summary string to inject as a system message, or null if summarization fails.</returns>
    Task<string?> SummarizeAsync(
        IReadOnlyList<ChatMessage> messagesToSummarize,
        CancellationToken cancellationToken = default);
}
