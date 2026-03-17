namespace SmartKb.Contracts.Models;

/// <summary>
/// Result of enrichment applied to a CanonicalRecord.
/// Fields augment the record's metadata for filterable retrieval and routing.
/// </summary>
public sealed record EnrichmentResult
{
    public string? Category { get; init; }
    public string? ProductArea { get; init; }
    public string? Severity { get; init; }
    public string? Environment { get; init; }
    public IReadOnlyList<string> ErrorTokens { get; init; } = [];
    public IReadOnlyList<string> PiiFlags { get; init; } = [];
    public int EnrichmentVersion { get; init; } = 1;

    /// <summary>Detected technology/framework tags (e.g., "Azure SQL", "React", ".NET").</summary>
    public IReadOnlyList<string> TechnologyTags { get; init; } = [];

    /// <summary>Detected component or module name within the product area.</summary>
    public string? Component { get; init; }

    /// <summary>Confidence score for the detected category (0.0-1.0). Higher means more signals matched.</summary>
    public float CategoryConfidence { get; init; }
}
