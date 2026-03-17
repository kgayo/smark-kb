namespace SmartKb.Contracts.Configuration;

/// <summary>
/// Configuration for case-card quality validation gates (P1-011).
/// Controls minimum thresholds for distilled patterns to pass quality checks.
/// </summary>
public sealed class CaseCardQualitySettings
{
    public const string SectionName = "CaseCardQuality";

    /// <summary>Minimum title length (characters) for a pattern to pass quality.</summary>
    public int MinTitleLength { get; set; } = 10;

    /// <summary>Maximum title length. Titles beyond this are flagged.</summary>
    public int MaxTitleLength { get; set; } = 200;

    /// <summary>Minimum problem statement length (characters).</summary>
    public int MinProblemStatementLength { get; set; } = 20;

    /// <summary>Minimum number of symptoms required.</summary>
    public int MinSymptomCount { get; set; } = 1;

    /// <summary>Minimum number of resolution steps required.</summary>
    public int MinResolutionStepCount { get; set; } = 1;

    /// <summary>Minimum character length for a resolution step to be considered meaningful.</summary>
    public int MinResolutionStepLength { get; set; } = 10;

    /// <summary>Minimum number of related evidence IDs.</summary>
    public int MinRelatedEvidenceCount { get; set; } = 1;

    /// <summary>
    /// Minimum overall quality score (0.0-1.0) for a pattern to pass the gate.
    /// Patterns below this threshold are flagged but still created as drafts.
    /// </summary>
    public float MinQualityScore { get; set; } = 0.3f;

    /// <summary>
    /// Quality score threshold below which patterns are rejected entirely
    /// (not saved to the database). Set to 0 to never reject.
    /// </summary>
    public float RejectThreshold { get; set; } = 0.15f;
}
