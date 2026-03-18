using SmartKb.Contracts.Services;

namespace SmartKb.Contracts.Tests;

/// <summary>
/// No-op text extraction service for tests that don't exercise binary extraction.
/// </summary>
public sealed class NullTextExtractionService : ITextExtractionService
{
    public bool CanExtract(string fileName) => false;

    public Task<TextExtractionResult> ExtractTextAsync(
        Stream content, string fileName, CancellationToken cancellationToken = default)
        => Task.FromResult(TextExtractionResult.Failure("No extraction in test mode."));
}
