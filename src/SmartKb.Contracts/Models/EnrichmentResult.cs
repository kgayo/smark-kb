namespace SmartKb.Contracts.Models;

/// <summary>
/// Result of baseline enrichment applied to a CanonicalRecord.
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
}
