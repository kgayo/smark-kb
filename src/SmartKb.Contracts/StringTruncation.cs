namespace SmartKb.Contracts;

/// <summary>
/// Named limits for diagnostic/display string truncation used throughout the application.
/// </summary>
public static class TruncationLimits
{
    /// <summary>SQL column limit for sync-run error detail.</summary>
    public const int ErrorDetail = 4000;

    /// <summary>HTTP response body preview in diagnostic logs and test-connection results.</summary>
    public const int DiagnosticBody = 500;

    /// <summary>Short error body embedded in user-facing error messages.</summary>
    public const int ErrorBodyShort = 200;

    /// <summary>Snippet preview for evidence chunks and pattern problem statements.</summary>
    public const int SnippetPreview = 200;

    /// <summary>Session auto-title derived from first user query.</summary>
    public const int SessionTitle = 100;
}

/// <summary>
/// Extension method for consistent string truncation.
/// </summary>
public static class StringTruncationExtensions
{
    /// <summary>
    /// Truncates <paramref name="value"/> to <paramref name="maxLength"/> characters,
    /// appending <paramref name="suffix"/> when truncation occurs.
    /// Returns the original string when it is within the limit.
    /// </summary>
    public static string Truncate(this string value, int maxLength, string suffix = "")
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            return value;

        return string.Concat(value.AsSpan(0, maxLength), suffix);
    }
}
