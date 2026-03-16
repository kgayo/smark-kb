namespace SmartKb.Contracts.Services;

/// <summary>
/// Persists evidence-to-answer trace links for audit and evaluation.
/// </summary>
public interface IAnswerTraceWriter
{
    Task WriteTraceAsync(
        Guid traceId,
        string tenantId,
        string userId,
        string correlationId,
        string query,
        string responseType,
        float confidence,
        string confidenceLabel,
        IReadOnlyList<string> citedChunkIds,
        IReadOnlyList<string> retrievedChunkIds,
        int aclFilteredOutCount,
        bool hasEvidence,
        bool escalationRecommended,
        string systemPromptVersion,
        long durationMs,
        CancellationToken cancellationToken = default);
}
