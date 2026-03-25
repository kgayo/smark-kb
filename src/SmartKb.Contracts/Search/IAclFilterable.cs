namespace SmartKb.Contracts.Search;

/// <summary>
/// Interface for search result types that support ACL security trimming.
/// Implemented by both RawSearchResult and RankedResult to enable shared filtering logic.
/// </summary>
internal interface IAclFilterable
{
    string Visibility { get; }
    IReadOnlyList<string> AllowedGroups { get; }
}
