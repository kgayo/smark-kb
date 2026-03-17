namespace SmartKb.Contracts.Models;

/// <summary>
/// Represents a solved-ticket session that qualifies for pattern distillation.
/// D-008: Status in (Closed, Resolved) AND ResolvedWithoutEscalation AND positive_feedback >= MinPositiveFeedback.
/// </summary>
public sealed record DistillationCandidate
{
    public required Guid SessionId { get; init; }
    public required string TenantId { get; init; }
    public required string UserId { get; init; }
    public string? SessionTitle { get; init; }

    /// <summary>Evidence IDs cited in the session's answer traces.</summary>
    public required IReadOnlyList<string> CitedEvidenceIds { get; init; }

    /// <summary>Chunk IDs cited across all answer traces in the session.</summary>
    public required IReadOnlyList<string> CitedChunkIds { get; init; }

    /// <summary>Count of ThumbsUp feedback on the session.</summary>
    public int PositiveFeedbackCount { get; init; }

    /// <summary>Count of ThumbsDown feedback on the session.</summary>
    public int NegativeFeedbackCount { get; init; }

    /// <summary>Product area from the evidence (if consistent across cited chunks).</summary>
    public string? ProductArea { get; init; }

    /// <summary>Tags collected from cited evidence chunks.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>When the session was resolved.</summary>
    public DateTimeOffset ResolvedAt { get; init; }

    /// <summary>Whether a pattern has already been distilled from this session.</summary>
    public bool AlreadyDistilled { get; init; }
}
