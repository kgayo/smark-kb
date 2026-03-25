namespace SmartKb.Contracts.Models;

/// <summary>
/// Valid resolution values for pattern contradictions.
/// </summary>
public static class ContradictionResolution
{
    public const string Merged = "Merged";
    public const string Deprecated = "Deprecated";
    public const string Kept = "Kept";
    public const string Dismissed = "Dismissed";
}
