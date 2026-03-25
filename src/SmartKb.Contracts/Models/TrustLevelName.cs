namespace SmartKb.Contracts.Models;

/// <summary>
/// Shared string constants for pattern trust levels. Mirrors the <see cref="Enums.TrustLevel"/>
/// enum values as strings for use in SQL entity defaults, EF Core LINQ queries, and
/// OData search index filters where enum values cannot be used directly.
/// </summary>
public static class TrustLevelName
{
    public const string Draft = "Draft";
    public const string Reviewed = "Reviewed";
    public const string Approved = "Approved";
    public const string Deprecated = "Deprecated";
}
