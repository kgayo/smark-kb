using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Search;

/// <summary>
/// Builds OData filter expressions for Azure AI Search queries from <see cref="RetrievalFilter"/>.
/// All filters are combined with AND. Collection filters use search.in() for multi-value OR.
/// </summary>
public static class ODataFilterBuilder
{
    /// <summary>
    /// Builds an OData filter string for the Evidence index from a <see cref="RetrievalFilter"/>.
    /// Returns null if the filter is empty or null.
    /// </summary>
    public static string? BuildEvidenceFilter(RetrievalFilter? filter)
    {
        if (filter is null || filter.IsEmpty) return null;

        var clauses = new List<string>();

        if (filter.SourceTypes is { Count: > 0 })
            clauses.Add(BuildSearchInClause(SearchFieldNames.SourceType, filter.SourceTypes));

        if (filter.ProductAreas is { Count: > 0 })
            clauses.Add(BuildSearchInClause(SearchFieldNames.ProductArea, filter.ProductAreas));

        if (filter.TimeHorizonDays is > 0)
        {
            var cutoff = DateTimeOffset.UtcNow.AddDays(-filter.TimeHorizonDays.Value);
            clauses.Add($"{SearchFieldNames.UpdatedAt} ge {cutoff:yyyy-MM-ddTHH:mm:ssZ}");
        }

        if (filter.Tags is { Count: > 0 })
            clauses.Add(BuildTagsAnyClause(SearchFieldNames.Tags, filter.Tags));

        if (filter.Statuses is { Count: > 0 })
            clauses.Add(BuildSearchInClause(SearchFieldNames.Status, filter.Statuses));

        return clauses.Count > 0 ? string.Join(" and ", clauses) : null;
    }

    /// <summary>
    /// Builds an OData filter string for the Pattern index from a <see cref="RetrievalFilter"/>.
    /// Only applies product_area, time_horizon, and tags (patterns have no source_type or status fields).
    /// Returns null if no applicable filters are set.
    /// </summary>
    public static string? BuildPatternFilter(RetrievalFilter? filter)
    {
        if (filter is null || filter.IsEmpty) return null;

        var clauses = new List<string>();

        if (filter.ProductAreas is { Count: > 0 })
            clauses.Add(BuildSearchInClause(PatternFieldNames.ProductArea, filter.ProductAreas));

        if (filter.TimeHorizonDays is > 0)
        {
            var cutoff = DateTimeOffset.UtcNow.AddDays(-filter.TimeHorizonDays.Value);
            clauses.Add($"{PatternFieldNames.UpdatedAt} ge {cutoff:yyyy-MM-ddTHH:mm:ssZ}");
        }

        if (filter.Tags is { Count: > 0 })
            clauses.Add(BuildTagsAnyClause(PatternFieldNames.Tags, filter.Tags));

        return clauses.Count > 0 ? string.Join(" and ", clauses) : null;
    }

    /// <summary>
    /// Combines a base filter (e.g., tenant isolation) with an optional additional filter using AND.
    /// </summary>
    public static string CombineFilters(string baseFilter, string? additionalFilter)
    {
        if (string.IsNullOrEmpty(additionalFilter)) return baseFilter;
        return $"{baseFilter} and {additionalFilter}";
    }

    /// <summary>Builds search.in() clause for a string field with multiple values (OR semantics).</summary>
    internal static string BuildSearchInClause(string fieldName, IReadOnlyList<string> values)
    {
        var escaped = string.Join(",", values.Select(v => EscapeODataSearchInValue(v)));
        return $"search.in({fieldName}, '{escaped}', ',')";
    }

    /// <summary>Builds any() lambda clause for a collection field with multiple values (OR semantics).</summary>
    internal static string BuildTagsAnyClause(string fieldName, IReadOnlyList<string> values)
    {
        var escaped = string.Join(",", values.Select(v => EscapeODataSearchInValue(v)));
        return $"{fieldName}/any(t: search.in(t, '{escaped}', ','))";
    }

    /// <summary>Escapes single quotes in OData filter values.</summary>
    public static string EscapeODataValue(string value) => value.Replace("'", "''");

    /// <summary>Escapes values for search.in() delimiter-separated list (single quotes and commas).</summary>
    private static string EscapeODataSearchInValue(string value) =>
        value.Replace("'", "''").Replace(",", "");
}
