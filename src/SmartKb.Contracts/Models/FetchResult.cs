namespace SmartKb.Contracts.Models;

/// <summary>
/// Result of a connector fetch operation (incremental or backfill).
/// </summary>
public sealed record FetchResult
{
    public required IReadOnlyList<CanonicalRecord> Records { get; init; }
    public required int FailedRecords { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
    public string? NewCheckpoint { get; init; }
    public bool HasMore { get; init; }
}
