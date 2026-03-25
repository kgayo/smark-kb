using System.Text.Json;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts;

namespace SmartKb.Data;

/// <summary>
/// Shared helper for extracting pattern IDs from cited-chunk-ID JSON arrays.
/// Consolidates duplicate ExtractPatternIds methods from PatternUsageMetricsService and PatternMaintenanceService.
/// </summary>
public static class PatternIdHelper
{
    /// <summary>
    /// Deserializes a JSON array of chunk IDs and returns the subset that are pattern IDs (prefixed with "pattern-").
    /// Uses case-insensitive comparison for both JSON deserialization and HashSet membership.
    /// </summary>
    public static HashSet<string> ExtractPatternIds(string? citedChunkIdsJson, ILogger? logger = null)
    {
        if (string.IsNullOrEmpty(citedChunkIdsJson)) return [];
        try
        {
            var ids = JsonSerializer.Deserialize<List<string>>(citedChunkIdsJson, SharedJsonOptions.CaseInsensitive) ?? [];
            return ids.Where(id => id.StartsWith("pattern-", StringComparison.OrdinalIgnoreCase))
                      .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException ex)
        {
            logger?.LogWarning(ex, "Failed to deserialize cited chunk IDs JSON in {MethodName}", nameof(ExtractPatternIds));
            return [];
        }
    }
}
