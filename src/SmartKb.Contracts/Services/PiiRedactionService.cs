using System.Text.RegularExpressions;

namespace SmartKb.Contracts.Services;

/// <summary>
/// Phase 1 baseline PII redaction: regex-based replacement of emails, phone numbers,
/// SSNs, and credit card numbers with type-specific placeholders (P0-014A, jtbd-10).
/// Reuses the same regex patterns as <see cref="BaselineEnrichmentService.DetectPii"/>.
/// </summary>
public sealed partial class PiiRedactionService : IPiiRedactionService
{
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

    // Same patterns as BaselineEnrichmentService — keep in sync.

    [GeneratedRegex(@"\b[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}\b", RegexOptions.Compiled)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"(?<!\d)(?:\+?1[-.\s]?)?(?:\(?\d{3}\)?[-.\s]?)?\d{3}[-.\s]?\d{4}(?!\d)", RegexOptions.Compiled)]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"\b\d{3}-\d{2}-\d{4}\b", RegexOptions.Compiled)]
    private static partial Regex SsnRegex();

    [GeneratedRegex(@"\b(?:\d{4}[-\s]?){3,4}\d{1,4}\b", RegexOptions.Compiled)]
    private static partial Regex CreditCardRegex();
}
