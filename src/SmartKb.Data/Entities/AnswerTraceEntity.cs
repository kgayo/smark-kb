namespace SmartKb.Data.Entities;

/// <summary>
/// Persists evidence-to-answer trace links for audit, evaluation, and debugging.
/// One row per chat orchestration response. Immutable once written.
/// </summary>
public sealed class AnswerTraceEntity
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string Query { get; set; } = string.Empty;
    public string ResponseType { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public string ConfidenceLabel { get; set; } = string.Empty;

    /// <summary>JSON array of chunk IDs cited in the response.</summary>
    public string CitedChunkIds { get; set; } = "[]";

    /// <summary>JSON array of all chunk IDs returned by retrieval (before citation selection).</summary>
    public string RetrievedChunkIds { get; set; } = "[]";

    public int RetrievedChunkCount { get; set; }
    public int AclFilteredOutCount { get; set; }
    public bool HasEvidence { get; set; }
    public bool EscalationRecommended { get; set; }
    public string SystemPromptVersion { get; set; } = string.Empty;
    public long DurationMs { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Unix epoch seconds of <see cref="CreatedAt"/>. Enables server-side filtering in SQLite (which cannot compare DateTimeOffset).</summary>
    public long CreatedAtEpoch { get; set; }
}
