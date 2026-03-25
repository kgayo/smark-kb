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

    /// <summary>
    /// Creates a FetchResult representing a single error with no records.
    /// </summary>
    public static FetchResult Error(string error) => new()
    {
        Records = [],
        FailedRecords = 0,
        Errors = [error],
        HasMore = false,
    };
}
