using System.Text.RegularExpressions;
using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

/// <summary>
/// PII redaction with policy-aware configuration (P0-014A baseline + P2-001 policy controls).
/// Supports built-in patterns (email, phone, SSN, credit card) and custom tenant-defined patterns.
/// Enforcement modes: "redact" (replace), "detect" (count only), "disabled" (skip).
/// </summary>
public sealed partial class PiiRedactionService : IPiiRedactionService
{
    private static readonly Dictionary<string, (Func<Regex> Regex, string Placeholder)> BuiltInPatterns = new()
    {
        ["email"] = (EmailRegex, "[REDACTED-EMAIL]"),
        ["phone"] = (PhoneRegex, "[REDACTED-PHONE]"),
        ["ssn"] = (SsnRegex, "[REDACTED-SSN]"),
        ["credit_card"] = (CreditCardRegex, "[REDACTED-CREDIT-CARD]"),
    };

    /// <summary>
    /// Default redaction: all built-in patterns enabled, redact mode (backward-compatible).
    /// </summary>
    public PiiRedactionResult Redact(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new PiiRedactionResult
            {
                RedactedText = text ?? string.Empty,
                RedactionCounts = new Dictionary<string, int>(),
            };
        }

        var counts = new Dictionary<string, int>();
        var result = text;

        // Order matters: redact credit cards before phone numbers to avoid partial matches.
        result = ReplaceAndCount(result, CreditCardRegex(), "[REDACTED-CREDIT-CARD]", "credit_card", counts);
        result = ReplaceAndCount(result, SsnRegex(), "[REDACTED-SSN]", "ssn", counts);
        result = ReplaceAndCount(result, EmailRegex(), "[REDACTED-EMAIL]", "email", counts);
        result = ReplaceAndCount(result, PhoneRegex(), "[REDACTED-PHONE]", "phone", counts);

        return new PiiRedactionResult
        {
            RedactedText = result,
            RedactionCounts = counts,
        };
    }

    /// <summary>
    /// Policy-aware redaction (P2-001). Respects enforcement mode, enabled types, and custom patterns.
    /// </summary>
    public PiiRedactionResult Redact(string text, PiiPolicyResponse policy)
    {
        if (string.IsNullOrEmpty(text) || policy.EnforcementMode == "disabled")
        {
            return new PiiRedactionResult
            {
                RedactedText = text ?? string.Empty,
                RedactionCounts = new Dictionary<string, int>(),
            };
        }

        var counts = new Dictionary<string, int>();
        var result = text;
        var detectOnly = policy.EnforcementMode == "detect";

        // Apply built-in patterns in priority order (credit_card first to avoid partial phone matches).
        string[] orderedTypes = ["credit_card", "ssn", "email", "phone"];
        foreach (var piiType in orderedTypes)
        {
            if (!policy.EnabledPiiTypes.Contains(piiType)) continue;
            if (!BuiltInPatterns.TryGetValue(piiType, out var pattern)) continue;

            if (detectOnly)
                DetectAndCount(result, pattern.Regex(), piiType, counts);
            else
                result = ReplaceAndCount(result, pattern.Regex(), pattern.Placeholder, piiType, counts);
        }

        // Apply custom patterns.
        foreach (var custom in policy.CustomPatterns)
        {
            try
            {
                var regex = new Regex(custom.Pattern, RegexOptions.Compiled, TimeSpan.FromSeconds(1));
                if (detectOnly)
                    DetectAndCount(result, regex, custom.Name, counts);
                else
                    result = ReplaceAndCount(result, regex, custom.Placeholder, custom.Name, counts);
            }
            catch (RegexMatchTimeoutException)
            {
                // Skip patterns that time out — don't fail the entire redaction.
            }
        }

        return new PiiRedactionResult
        {
            RedactedText = detectOnly ? text : result,
            RedactionCounts = counts,
        };
    }

    private static string ReplaceAndCount(
        string input, Regex regex, string replacement, string piiType, Dictionary<string, int> counts)
    {
        var matches = regex.Matches(input);
        if (matches.Count > 0)
        {
            counts[piiType] = matches.Count;
            return regex.Replace(input, replacement);
        }
        return input;
    }

    private static void DetectAndCount(
        string input, Regex regex, string piiType, Dictionary<string, int> counts)
    {
        var matches = regex.Matches(input);
        if (matches.Count > 0)
            counts[piiType] = matches.Count;
    }

    // Same patterns as EnhancedEnrichmentService — keep in sync.

    [GeneratedRegex(@"\b[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}\b", RegexOptions.Compiled)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"(?<!\d)(?:\+?1[-.\s]?)?(?:\(?\d{3}\)?[-.\s]?)?\d{3}[-.\s]?\d{4}(?!\d)", RegexOptions.Compiled)]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"\b\d{3}-\d{2}-\d{4}\b", RegexOptions.Compiled)]
    private static partial Regex SsnRegex();

    [GeneratedRegex(@"\b(?:\d{4}[-\s]?){3,4}\d{1,4}\b", RegexOptions.Compiled)]
    private static partial Regex CreditCardRegex();
}
