using System.Text;
using System.Text.RegularExpressions;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

/// <summary>
/// Splits text into overlapping chunks respecting markdown structural boundaries
/// (headers, code blocks, list items) with token-based fallback for unstructured content.
/// Token estimation: ~4 characters per token (GPT-family approximation).
/// </summary>
public sealed partial class TextChunkingService : IChunkingService
{
    private const int CharsPerToken = 4;
    private static readonly ChunkingSettings DefaultSettings = new();

    public IReadOnlyList<TextChunk> Chunk(string text, string? title = null, ChunkingSettings? settings = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [new TextChunk(text ?? string.Empty, title, 0)];

        settings ??= DefaultSettings;
        var maxChars = settings.MaxTokensPerChunk * CharsPerToken;
        var overlapChars = settings.OverlapTokens * CharsPerToken;

        // If entire text fits in one chunk, return as-is.
        if (text.Length <= maxChars)
            return [new TextChunk(text, title, 0)];

        var sections = settings.UseStructuralBoundaries
            ? SplitByStructure(text)
            : [new Section(null, text)];

        var chunks = new List<TextChunk>();
        var buffer = new StringBuilder();
        string? currentContext = title;
        int chunkIndex = 0;

        foreach (var section in sections)
        {
            var sectionContext = section.Header ?? currentContext;

            // If adding this section would exceed max, flush the buffer first.
            if (buffer.Length > 0 && buffer.Length + section.Body.Length > maxChars)
            {
                chunks.Add(new TextChunk(buffer.ToString().TrimEnd(), currentContext, chunkIndex++));

                // Overlap: carry tail of previous chunk into next.
                var tail = GetOverlapTail(buffer.ToString(), overlapChars);
                buffer.Clear();
                if (tail.Length > 0)
                    buffer.Append(tail);
            }

            currentContext = sectionContext;

            // If section itself exceeds max, split it by paragraphs/sentences.
            if (section.Body.Length > maxChars)
            {
                var subParts = SplitLargeSection(section.Body, maxChars, overlapChars);
                foreach (var part in subParts)
                {
                    if (buffer.Length > 0 && buffer.Length + part.Length > maxChars)
                    {
                        chunks.Add(new TextChunk(buffer.ToString().TrimEnd(), currentContext, chunkIndex++));
                        var tail = GetOverlapTail(buffer.ToString(), overlapChars);
                        buffer.Clear();
                        if (tail.Length > 0)
                            buffer.Append(tail);
                    }
                    buffer.Append(part);
                }
            }
            else
            {
                buffer.Append(section.Body);
            }
        }

        // Flush remaining buffer.
        if (buffer.Length > 0)
        {
            var remaining = buffer.ToString().TrimEnd();
            if (remaining.Length > 0)
                chunks.Add(new TextChunk(remaining, currentContext, chunkIndex));
        }

        // Edge case: if we somehow ended up with no chunks, return the full text.
        if (chunks.Count == 0)
            return [new TextChunk(text, title, 0)];

        return chunks;
    }

    private static List<Section> SplitByStructure(string text)
    {
        var sections = new List<Section>();
        var lines = text.Split('\n');
        var currentHeader = (string?)null;
        var body = new StringBuilder();

        foreach (var line in lines)
        {
            if (MarkdownHeaderRegex().IsMatch(line))
            {
                // Flush previous section.
                if (body.Length > 0)
                {
                    sections.Add(new Section(currentHeader, body.ToString()));
                    body.Clear();
                }
                currentHeader = line.TrimStart('#', ' ');
                body.Append(line).Append('\n');
            }
            else
            {
                body.Append(line).Append('\n');
            }
        }

        if (body.Length > 0)
            sections.Add(new Section(currentHeader, body.ToString()));

        return sections;
    }

    private static List<string> SplitLargeSection(string text, int maxChars, int overlapChars)
    {
        var parts = new List<string>();

        // Split by double-newline (paragraphs) first.
        var paragraphs = ParagraphSplitRegex().Split(text);
        var buffer = new StringBuilder();

        foreach (var para in paragraphs)
        {
            if (string.IsNullOrWhiteSpace(para)) continue;

            if (buffer.Length > 0 && buffer.Length + para.Length + 2 > maxChars)
            {
                parts.Add(buffer.ToString());
                var tail = GetOverlapTail(buffer.ToString(), overlapChars);
                buffer.Clear();
                if (tail.Length > 0)
                    buffer.Append(tail);
            }

            // If a single paragraph exceeds max, hard-split by character boundary.
            if (para.Length > maxChars)
            {
                if (buffer.Length > 0)
                {
                    parts.Add(buffer.ToString());
                    buffer.Clear();
                }

                var offset = 0;
                while (offset < para.Length)
                {
                    var end = Math.Min(offset + maxChars, para.Length);
                    // Try to break at a word boundary.
                    if (end < para.Length)
                    {
                        var lastSpace = para.LastIndexOf(' ', end, Math.Min(end - offset, 200));
                        if (lastSpace > offset)
                            end = lastSpace + 1;
                    }
                    parts.Add(para[offset..end]);
                    offset = Math.Max(offset + 1, end - overlapChars);
                }
            }
            else
            {
                if (buffer.Length > 0)
                    buffer.Append("\n\n");
                buffer.Append(para);
            }
        }

        if (buffer.Length > 0)
            parts.Add(buffer.ToString());

        return parts;
    }

    private static string GetOverlapTail(string text, int overlapChars)
    {
        if (text.Length <= overlapChars)
            return text;

        var start = text.Length - overlapChars;
        // Try to start at a word boundary.
        var nextSpace = text.IndexOf(' ', start);
        if (nextSpace >= 0 && nextSpace < text.Length - 1)
            start = nextSpace + 1;

        return text[start..];
    }

    private sealed record Section(string? Header, string Body);

    [GeneratedRegex(@"^#{1,6}\s", RegexOptions.Compiled)]
    private static partial Regex MarkdownHeaderRegex();

    [GeneratedRegex(@"\n{2,}", RegexOptions.Compiled)]
    private static partial Regex ParagraphSplitRegex();
}
