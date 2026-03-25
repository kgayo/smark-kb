using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Search;

/// <summary>
/// Shared ACL security trimming logic for search results.
/// Restricted documents are only returned if the user is in at least one allowed group.
/// Internal and public documents pass through for all authenticated users.
/// CRITICAL: This ensures restricted content never reaches the model (jtbd-03, jtbd-10).
/// </summary>
internal static class AclFilterHelper
{
    internal static (List<T> Filtered, int FilteredOutCount) ApplyAclFilter<T>(
        List<T> results,
        IReadOnlyList<string>? userGroups) where T : IAclFilterable
    {
        var filtered = new List<T>();
        var filteredOut = 0;
        var groupSet = userGroups is { Count: > 0 }
            ? new HashSet<string>(userGroups, StringComparer.OrdinalIgnoreCase)
            : null;

        foreach (var result in results)
        {
            // Public and Internal visibility: accessible to all authenticated users.
            if (!string.Equals(result.Visibility, VisibilityLevel.Restricted, StringComparison.OrdinalIgnoreCase))
            {
                filtered.Add(result);
                continue;
            }

            // Restricted visibility: user must be in at least one allowed group.
            if (groupSet is not null && result.AllowedGroups.Any(g => groupSet.Contains(g)))
            {
                filtered.Add(result);
            }
            else
            {
                filteredOut++;
            }
        }

        return (filtered, filteredOut);
    }
}
