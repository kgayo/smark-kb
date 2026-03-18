namespace SmartKb.Contracts.Configuration;

/// <summary>
/// Configuration for pattern maintenance automation and contradiction detection (P2-004).
/// </summary>
public sealed class PatternMaintenanceSettings
{
    public const string SectionName = "PatternMaintenance";

    /// <summary>Days since last update for a pattern to be considered stale.</summary>
    public int StaleDaysThreshold { get; set; } = 90;

    /// <summary>Quality score below which a pattern is flagged for maintenance.</summary>
    public float LowQualityThreshold { get; set; } = 0.4f;

    /// <summary>
    /// Minimum symptom token overlap ratio (0-1) to consider two patterns as addressing
    /// the same problem domain for contradiction detection.
    /// </summary>
    public float SymptomOverlapThreshold { get; set; } = 0.4f;

    /// <summary>
    /// Minimum Jaccard similarity of resolution step tokens to consider resolutions
    /// as conflicting (diverging from each other despite similar symptoms).
    /// </summary>
    public float ResolutionDivergenceThreshold { get; set; } = 0.7f;

    /// <summary>
    /// Minimum title/problem token overlap ratio to flag as potential duplicate.
    /// </summary>
    public float DuplicateThreshold { get; set; } = 0.6f;

    /// <summary>Days with no citation in answer traces to flag as unused.</summary>
    public int UnusedDaysThreshold { get; set; } = 60;

    /// <summary>Maximum number of pattern pairs to compare per detection run.</summary>
    public int MaxComparisonPairs { get; set; } = 5000;
}
