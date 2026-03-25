namespace SmartKb.Contracts.Models;

/// <summary>
/// Shared string constants for visibility levels used in evidence records,
/// case patterns, and ACL security trimming. Mirrors the <see cref="Enums.AccessVisibility"/>
/// enum values as strings for use in SQL entities, search index documents, and
/// OrdinalIgnoreCase comparisons.
/// </summary>
public static class VisibilityLevel
{
    public const string Internal = "Internal";
    public const string Restricted = "Restricted";
    public const string Public = "Public";
}
