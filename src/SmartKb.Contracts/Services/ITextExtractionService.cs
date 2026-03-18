namespace SmartKb.Contracts.Services;

/// <summary>
/// Extracts text content from binary document formats (PDF, DOCX, PPTX, XLSX).
/// Used in the ingestion pipeline to make binary documents searchable.
/// </summary>
public interface ITextExtractionService
{
    /// <summary>
    /// Extracts text from a binary document stream.
    /// </summary>
    /// <param name="content">The binary document content.</param>
    /// <param name="fileName">File name with extension (used for format detection).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Extraction result with text content or error detail.</returns>
    Task<TextExtractionResult> ExtractTextAsync(
        Stream content,
        string fileName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true if the service can extract text from the given file type.
    /// </summary>
    bool CanExtract(string fileName);
}

/// <summary>
/// Result of a text extraction operation.
/// </summary>
public sealed record TextExtractionResult
{
    public required bool Success { get; init; }
    public string Text { get; init; } = string.Empty;
    public string? Error { get; init; }
    public int PageCount { get; init; }
    public string Format { get; init; } = string.Empty;

    public static TextExtractionResult Failure(string error, string format = "") =>
        new() { Success = false, Error = error, Format = format };

    public static TextExtractionResult Ok(string text, int pageCount, string format) =>
        new() { Success = true, Text = text, PageCount = pageCount, Format = format };
}
