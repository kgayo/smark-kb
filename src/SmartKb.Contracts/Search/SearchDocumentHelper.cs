using Azure.Search.Documents.Models;

namespace SmartKb.Contracts.Search;

/// <summary>
/// Shared helpers for extracting typed values from Azure AI Search <see cref="SearchDocument"/> results.
/// Used by both AzureSearchRetrievalService and FusedRetrievalService.
/// </summary>
internal static class SearchDocumentHelper
{
    internal static string GetString(SearchDocument doc, string key) =>
        doc.TryGetValue(key, out var val) && val is string s ? s : string.Empty;

    internal static string? GetStringOrNull(SearchDocument doc, string key) =>
        doc.TryGetValue(key, out var val) && val is string s && !string.IsNullOrEmpty(s) ? s : null;

    internal static IReadOnlyList<string> GetStringList(SearchDocument doc, string key)
    {
        if (!doc.TryGetValue(key, out var val)) return [];
        if (val is IEnumerable<string> strings) return strings.ToList();
        if (val is IEnumerable<object> objects) return objects.Select(o => o?.ToString() ?? "").ToList();
        return [];
    }
}
