namespace SmartKb.Contracts;

/// <summary>
/// Custom MIME media type constants not available in <see cref="System.Net.Mime.MediaTypeNames"/>.
/// </summary>
public static class CustomMediaTypes
{
    /// <summary>Newline-delimited JSON (NDJSON) media type for streaming exports.</summary>
    public const string Ndjson = "application/x-ndjson";

    /// <summary>Default content type for raw text content stored in blob storage.</summary>
    public const string TextPlainUtf8 = "text/plain; charset=utf-8";
}
