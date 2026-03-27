namespace SmartKb.Contracts.Models;

/// <summary>
/// Shared validation for eval case IDs. Format: "eval-NNNNN" (prefix + exactly 5 digits, total length 10).
/// </summary>
public static class EvalCaseId
{
    public const string Prefix = "eval-";
    public const int ExpectedLength = 10;

    /// <summary>
    /// Returns true if the case ID matches the "eval-NNNNN" format (exactly 10 characters).
    /// </summary>
    public static bool IsValid(string? caseId)
    {
        return !string.IsNullOrWhiteSpace(caseId)
            && caseId.StartsWith(Prefix, StringComparison.Ordinal)
            && caseId.Length == ExpectedLength;
    }
}
