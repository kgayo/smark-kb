using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

/// <summary>
/// Detects and redacts PII patterns in text. Used by the orchestration layer to ensure
/// PII is masked before model context assembly (P0-014A, jtbd-10).
/// </summary>
public interface IPiiRedactionService
{
    /// <summary>
    /// Redacts PII patterns in the given text using all built-in patterns (default behavior).
    /// </summary>
    PiiRedactionResult Redact(string text);

    /// <summary>
    /// Redacts PII patterns according to a tenant's PII policy configuration (P2-001).
    /// When enforcementMode is "detect", returns counts but does not modify text.
    /// When enforcementMode is "disabled", returns original text with no redactions.
    /// </summary>
    PiiRedactionResult Redact(string text, PiiPolicyResponse policy);
}

/// <summary>
/// Result of a PII redaction operation, including the redacted text and counts by PII type.
/// </summary>
public sealed record PiiRedactionResult
{
    public required string RedactedText { get; init; }
    public required IReadOnlyDictionary<string, int> RedactionCounts { get; init; }
    public int TotalRedactions => RedactionCounts.Values.Sum();
}
