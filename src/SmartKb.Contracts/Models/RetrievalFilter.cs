namespace SmartKb.Contracts.Models;

/// <summary>
/// Optional filters applied to retrieval queries to narrow results by metadata.
/// All filters are combined with AND logic. Null/empty values are ignored.
/// PRD: source_type_preference, time_horizon_days, product_area filtering.
/// </summary>
public sealed record RetrievalFilter
{
    /// <summary>
    /// Filter by source types (e.g., "Ticket", "Document", "WikiPage", "CasePattern").
    /// When set, only results matching one of these source types are returned.
    /// </summary>
    public IReadOnlyList<string>? SourceTypes { get; init; }

    /// <summary>
    /// Filter by product areas (e.g., "Auth", "Billing", "Integrations").
    /// When set, only results matching one of these product areas are returned.
    /// </summary>
    public IReadOnlyList<string>? ProductAreas { get; init; }

    /// <summary>
    /// Filter by maximum age in days. Results older than this are excluded.
    /// E.g., 90 = only evidence from the last 90 days.
    /// </summary>
    public int? TimeHorizonDays { get; init; }

    /// <summary>
    /// Filter by tags. Results must contain at least one of the specified tags.
    /// </summary>
    public IReadOnlyList<string>? Tags { get; init; }

    /// <summary>
    /// Filter by evidence status (e.g., "Active", "Closed", "Resolved").
    /// When set, only results matching one of these statuses are returned.
    /// </summary>
    public IReadOnlyList<string>? Statuses { get; init; }

    /// <summary>Returns true if no filters are set.</summary>
    public bool IsEmpty =>
        (SourceTypes is null or { Count: 0 }) &&
        (ProductAreas is null or { Count: 0 }) &&
        TimeHorizonDays is null &&
        (Tags is null or { Count: 0 }) &&
        (Statuses is null or { Count: 0 });
}
