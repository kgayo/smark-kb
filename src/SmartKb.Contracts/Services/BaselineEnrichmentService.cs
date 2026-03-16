using System.Text.RegularExpressions;
using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

/// <summary>
/// Phase 1 baseline enrichment: keyword-based category/severity/environment extraction,
/// error token extraction (exception names, HTTP status codes, error codes),
/// and regex-based PII detection (emails, phone numbers, SSNs, credit card numbers).
/// </summary>
public sealed partial class BaselineEnrichmentService : IEnrichmentService
{
    public const int CurrentEnrichmentVersion = 1;

    public EnrichmentResult Enrich(CanonicalRecord record)
    {
        var text = $"{record.Title}\n{record.TextContent}";

        return new EnrichmentResult
        {
            Category = DetectCategory(text, record),
            ProductArea = record.ProductArea,
            Severity = record.Severity ?? DetectSeverity(text),
            Environment = DetectEnvironment(text),
            ErrorTokens = ExtractErrorTokens(text),
            PiiFlags = DetectPii(text),
            EnrichmentVersion = CurrentEnrichmentVersion,
        };
    }

    private static string? DetectCategory(string text, CanonicalRecord record)
    {
        var lower = text.ToLowerInvariant();

        // Use source type hints first.
        if (record.SourceType is Enums.SourceType.WikiPage)
            return "documentation";

        if (ContainsAny(lower, "bug", "defect", "regression", "broken", "crash", "exception", "error", "failure", "stack trace"))
            return "bug";
        if (ContainsAny(lower, "feature request", "enhancement", "improvement", "new feature"))
            return "feature_request";
        if (ContainsAny(lower, "incident", "outage", "downtime", "degradation", "p1", "sev1", "sev-1"))
            return "incident";
        if (ContainsAny(lower, "question", "how to", "how do", "help me", "guidance", "clarification"))
            return "question";
        if (ContainsAny(lower, "task", "action item", "todo", "to-do"))
            return "task";

        return null;
    }

    private static string? DetectSeverity(string text)
    {
        var lower = text.ToLowerInvariant();

        if (ContainsAny(lower, "critical", "sev-1", "sev1", "p0", "p1", "severity: critical", "priority: 1"))
            return "critical";
        if (ContainsAny(lower, "high", "sev-2", "sev2", "p2", "severity: high", "priority: 2"))
            return "high";
        if (ContainsAny(lower, "medium", "sev-3", "sev3", "p3", "severity: medium", "priority: 3"))
            return "medium";
        if (ContainsAny(lower, "low", "sev-4", "sev4", "p4", "severity: low", "priority: 4", "minor"))
            return "low";

        return null;
    }

    private static string? DetectEnvironment(string text)
    {
        var lower = text.ToLowerInvariant();

        if (ContainsAny(lower, "production", "prod environment", "in prod", "prod instance"))
            return "production";
        if (ContainsAny(lower, "staging", "stage environment", "pre-prod", "preprod"))
            return "staging";
        if (ContainsAny(lower, "development", "dev environment", "dev instance", "local dev"))
            return "development";
        if (ContainsAny(lower, "test environment", "qa environment", "uat", "testing"))
            return "test";

        return null;
    }

    internal static IReadOnlyList<string> ExtractErrorTokens(string text)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // .NET/Java exception class names (e.g., NullReferenceException, IOException).
        foreach (Match m in ExceptionNameRegex().Matches(text))
            tokens.Add(m.Value);

        // HTTP status codes (e.g., 400, 401, 403, 404, 500, 502, 503).
        foreach (Match m in HttpStatusCodeRegex().Matches(text))
            tokens.Add($"HTTP {m.Groups[1].Value}");

        // Error codes (e.g., ERR-001, ERROR_CODE_123, E1234).
        foreach (Match m in ErrorCodeRegex().Matches(text))
            tokens.Add(m.Value);

        // Hex error codes (e.g., 0x80070005).
        foreach (Match m in HexErrorRegex().Matches(text))
            tokens.Add(m.Value);

        return tokens.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();
    }

    internal static IReadOnlyList<string> DetectPii(string text)
    {
        var flags = new List<string>();

        if (EmailRegex().IsMatch(text))
            flags.Add("email");
        if (PhoneRegex().IsMatch(text))
            flags.Add("phone");
        if (SsnRegex().IsMatch(text))
            flags.Add("ssn");
        if (CreditCardRegex().IsMatch(text))
            flags.Add("credit_card");

        return flags;
    }

    private static bool ContainsAny(string text, params string[] terms)
    {
        foreach (var term in terms)
        {
            if (text.Contains(term, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    // Exception names: PascalCase words ending with Exception, Error, Fault.
    [GeneratedRegex(@"\b[A-Z][a-zA-Z]+(?:Exception|Error|Fault)\b", RegexOptions.Compiled)]
    private static partial Regex ExceptionNameRegex();

    // HTTP status codes in common patterns like "HTTP 500", "status 404", "returned 503".
    [GeneratedRegex(@"(?:HTTP|status|returned|response|code)\s*([45]\d{2})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex HttpStatusCodeRegex();

    // Error codes like ERR-001, ERROR_CODE_123, AADSTS50076.
    [GeneratedRegex(@"\b(?:ERR[-_]?\d+|ERROR[-_][A-Z_]+\d*|[A-Z]{2,}STS\d+|E\d{4,})\b", RegexOptions.Compiled)]
    private static partial Regex ErrorCodeRegex();

    // Hex error codes like 0x80070005.
    [GeneratedRegex(@"\b0x[0-9A-Fa-f]{4,}\b", RegexOptions.Compiled)]
    private static partial Regex HexErrorRegex();

    // Email addresses.
    [GeneratedRegex(@"\b[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}\b", RegexOptions.Compiled)]
    private static partial Regex EmailRegex();

    // Phone numbers (US-style: 10+ digits with optional separators).
    [GeneratedRegex(@"(?<!\d)(?:\+?1[-.\s]?)?(?:\(?\d{3}\)?[-.\s]?)?\d{3}[-.\s]?\d{4}(?!\d)", RegexOptions.Compiled)]
    private static partial Regex PhoneRegex();

    // SSN pattern (xxx-xx-xxxx).
    [GeneratedRegex(@"\b\d{3}-\d{2}-\d{4}\b", RegexOptions.Compiled)]
    private static partial Regex SsnRegex();

    // Credit card numbers (13-19 digits with optional separators).
    [GeneratedRegex(@"\b(?:\d{4}[-\s]?){3,4}\d{1,4}\b", RegexOptions.Compiled)]
    private static partial Regex CreditCardRegex();
}
