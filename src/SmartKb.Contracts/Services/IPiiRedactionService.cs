namespace SmartKb.Contracts.Services;

/// <summary>
/// Detects and redacts PII patterns in text. Used by the orchestration layer to ensure
/// PII is masked before model context assembly (P0-014A, jtbd-10).
/// </summary>
public interface IPiiRedactionService
{
    /// <summary>
    /// Redacts PII patterns in the given text, returning the redacted text and a summary of what was redacted.
    /// </summary>
    PiiRedactionResult Redact(string text);
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
