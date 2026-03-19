using System.Text;
using System.Text.RegularExpressions;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

/// <summary>
/// Splits text into overlapping chunks respecting markdown structural boundaries
/// (headers, code blocks, list items) with token-based fallback for unstructured content.
/// For tickets and work items, applies troubleshooting-structure chunking that splits on
/// standard ticket section patterns (symptoms, root cause, resolution, verification).
/// Token estimation: ~4 characters per token (GPT-family approximation).
/// </summary>
public sealed partial class TextChunkingService : IChunkingService
{
    private const int CharsPerToken = 4;
    private static readonly ChunkingSettings DefaultSettings = new();

    public IReadOnlyList<TextChunk> Chunk(string text, string? title = null, ChunkingSettings? settings = null, SourceType? sourceType = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [new TextChunk(text ?? string.Empty, title, 0)];

        settings ??= DefaultSettings;
        var maxChars = settings.MaxTokensPerChunk * CharsPerToken;
        var overlapChars = settings.OverlapTokens * CharsPerToken;

        // If entire text fits in one chunk, return as-is.
        if (text.Length <= maxChars)
            return [new TextChunk(text, title, 0)];

        // Dispatch to ticket-structure chunking for tickets and work items.
        if (sourceType is SourceType.Ticket or SourceType.WorkItem && settings.UseStructuralBoundaries)
            return ChunkByTicketStructure(text, title, maxChars, overlapChars);

        var sections = settings.UseStructuralBoundaries
            ? SplitByStructure(text)
            : [new Section(null, text)];

        return BuildChunksFromSections(sections, title, maxChars, overlapChars);
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

    /// <summary>
    /// Splits ticket/work-item text by troubleshooting-structure section boundaries.
    /// Detects common ticket narrative patterns: symptoms/description, steps to reproduce,
    /// root cause/analysis, resolution/workaround, and verification sections.
    /// Each detected section becomes a chunk (or is further split if oversized).
    /// Falls back to generic structural chunking if no ticket sections are detected.
    /// </summary>
    private IReadOnlyList<TextChunk> ChunkByTicketStructure(string text, string? title, int maxChars, int overlapChars)
    {
        var sections = SplitByTicketSections(text);

        // Fallback: if no ticket sections detected (single section with no header),
        // use generic structural chunking.
        if (sections.Count <= 1 && sections[0].Header is null)
        {
            var genericSections = SplitByStructure(text);
            return BuildChunksFromSections(genericSections, title, maxChars, overlapChars);
        }

        return BuildChunksFromSections(sections, title, maxChars, overlapChars);
    }

    private IReadOnlyList<TextChunk> BuildChunksFromSections(List<Section> sections, string? title, int maxChars, int overlapChars)
    {
        var chunks = new List<TextChunk>();
        var buffer = new StringBuilder();
        string? currentContext = title;
        int chunkIndex = 0;

        foreach (var section in sections)
        {
            var sectionContext = section.Header ?? currentContext;

            if (buffer.Length > 0 && buffer.Length + section.Body.Length > maxChars)
            {
                chunks.Add(new TextChunk(buffer.ToString().TrimEnd(), currentContext, chunkIndex++));
                var tail = GetOverlapTail(buffer.ToString(), overlapChars);
                buffer.Clear();
                if (tail.Length > 0)
                    buffer.Append(tail);
            }

            currentContext = sectionContext;

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

        if (buffer.Length > 0)
        {
            var remaining = buffer.ToString().TrimEnd();
            if (remaining.Length > 0)
                chunks.Add(new TextChunk(remaining, currentContext, chunkIndex));
        }

        if (chunks.Count == 0)
            return [new TextChunk(string.Join("", sections.Select(s => s.Body)), title, 0)];

        return chunks;
    }

    private static List<Section> SplitByTicketSections(string text)
    {
        var sections = new List<Section>();
        var lines = text.Split('\n');
        string? currentHeader = null;
        var body = new StringBuilder();

        foreach (var line in lines)
        {
            var sectionLabel = DetectTicketSectionHeader(line);
            if (sectionLabel is not null)
            {
                // Flush previous section.
                if (body.Length > 0)
                {
                    sections.Add(new Section(currentHeader, body.ToString()));
                    body.Clear();
                }
                currentHeader = sectionLabel;
                body.Append(line).Append('\n');
            }
            else if (MarkdownHeaderRegex().IsMatch(line))
            {
                // Also respect markdown headers as secondary boundaries.
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

    /// <summary>
    /// Detects common ticket/work-item section headers by pattern matching.
    /// Returns a normalized section label or null if the line is not a section header.
    /// </summary>
    private static string? DetectTicketSectionHeader(string line)
    {
        var trimmed = line.Trim().TrimStart('#', ' ', '*', '-');
        if (trimmed.Length == 0) return null;

        // Remove trailing colons and whitespace for matching.
        var normalized = trimmed.TrimEnd(':', ' ').ToLowerInvariant();

        return normalized switch
        {
            // Symptoms / Description / Problem
            "description" or "problem description" or "issue description"
                or "problem" or "issue" or "summary" or "issue summary"
                or "symptoms" or "symptom" or "observed behavior"
                or "actual behavior" or "current behavior"
                => "Symptoms",

            // Steps to Reproduce
            "steps to reproduce" or "repro steps" or "reproduction steps"
                or "how to reproduce" or "steps" or "reproduce"
                or "repro" or "replication steps"
                => "Steps to Reproduce",

            // Expected Behavior
            "expected behavior" or "expected result" or "expected outcome"
                or "expected"
                => "Expected Behavior",

            // Root Cause / Analysis
            "root cause" or "root cause analysis" or "cause"
                or "analysis" or "investigation" or "diagnosis"
                or "findings" or "root-cause"
                => "Root Cause",

            // Resolution / Fix / Solution
            "resolution" or "solution" or "fix" or "fix applied"
                or "how it was fixed" or "corrective action"
                or "remediation" or "resolution steps"
                => "Resolution",

            // Workaround
            "workaround" or "work around" or "temporary fix"
                or "interim solution" or "mitigation"
                => "Workaround",

            // Verification / Testing
            "verification" or "verification steps" or "test steps"
                or "how to verify" or "testing" or "validation"
                or "acceptance criteria" or "test plan"
                => "Verification",

            // Impact / Severity
            "impact" or "business impact" or "customer impact"
                or "severity" or "affected users" or "affected area"
                or "scope of impact"
                => "Impact",

            // Environment / Context
            "environment" or "environment details" or "configuration"
                or "system information" or "system info" or "setup"
                or "context" or "additional context" or "additional information"
                => "Environment",

            _ => null,
        };
    }

    private sealed record Section(string? Header, string Body);

    [GeneratedRegex(@"^#{1,6}\s", RegexOptions.Compiled)]
    private static partial Regex MarkdownHeaderRegex();

    [GeneratedRegex(@"\n{2,}", RegexOptions.Compiled)]
    private static partial Regex ParagraphSplitRegex();
}
