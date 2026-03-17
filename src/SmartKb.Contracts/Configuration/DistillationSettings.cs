namespace SmartKb.Contracts.Configuration;

/// <summary>
/// Configuration for the solved-ticket pattern distillation pipeline (P1-005).
/// D-008 resolved: Status in (Closed, Resolved) AND ResolvedWithoutEscalation AND positive_feedback >= 1.
/// </summary>
public sealed class DistillationSettings
{
    public const string SectionName = "Distillation";

    /// <summary>
    /// Minimum number of positive feedback (ThumbsUp) required for a session
    /// to qualify as a distillation candidate.
    /// </summary>
    public int MinPositiveFeedback { get; set; } = 1;

    /// <summary>
    /// Evidence statuses that qualify as "solved" for distillation.
    /// Default: Closed (maps to Closed/Resolved source ticket statuses).
    /// </summary>
    public IReadOnlyList<string> SolvedStatuses { get; set; } = ["Closed"];

    /// <summary>
    /// Maximum number of candidates to return in a single query.
    /// </summary>
    public int MaxCandidates { get; set; } = 100;

    /// <summary>
    /// Maximum number of patterns to distill in a single batch run.
    /// </summary>
    public int MaxBatchSize { get; set; } = 20;

    /// <summary>
    /// Minimum number of evidence chunks a candidate must have cited
    /// to be eligible for distillation.
    /// </summary>
    public int MinCitedChunks { get; set; } = 1;

    /// <summary>
    /// Base confidence score assigned to distilled patterns.
    /// Adjusted by feedback ratio and evidence count.
    /// </summary>
    public float BaseConfidence { get; set; } = 0.5f;

    /// <summary>
    /// Confidence boost per additional positive feedback beyond the minimum.
    /// </summary>
    public float PositiveFeedbackBoost { get; set; } = 0.05f;

    /// <summary>
    /// Confidence penalty per negative feedback on the session.
    /// </summary>
    public float NegativeFeedbackPenalty { get; set; } = 0.1f;

    /// <summary>
    /// Maximum confidence score a distilled pattern can receive.
    /// </summary>
    public float MaxConfidence { get; set; } = 0.9f;
}
