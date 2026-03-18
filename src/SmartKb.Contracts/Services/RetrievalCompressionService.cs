using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

/// <summary>
/// Compresses evidence chunks before prompt assembly to reduce token usage (P2-003).
/// Truncates long chunk text while preserving leading content and adding truncation markers.
/// </summary>
public static class RetrievalCompressionService
{
    /// <summary>
    /// Compresses retrieved chunks by truncating text exceeding maxChars.
    /// Preserves chunk metadata. Returns new chunk instances where truncation was applied.
    /// </summary>
    public static (IReadOnlyList<RetrievedChunk> Compressed, int TruncatedCount) CompressChunks(
        IReadOnlyList<RetrievedChunk> chunks,
        int maxChunkChars)
    {
        if (maxChunkChars <= 0) return (chunks, 0);

        var result = new List<RetrievedChunk>(chunks.Count);
        var truncated = 0;

        foreach (var chunk in chunks)
        {
            if (chunk.ChunkText.Length > maxChunkChars)
            {
                truncated++;
                // Truncate at the last word boundary before maxChunkChars.
                var truncationPoint = FindWordBoundary(chunk.ChunkText, maxChunkChars);
                result.Add(chunk with
                {
                    ChunkText = chunk.ChunkText[..truncationPoint] + " [...]",
                });
            }
            else
            {
                result.Add(chunk);
            }
        }

        return (result, truncated);
    }

    /// <summary>
    /// Finds the last space or newline before the target position to avoid cutting mid-word.
    /// Falls back to the target position if no boundary found in the last 100 chars.
    /// </summary>
    internal static int FindWordBoundary(string text, int target)
    {
        if (target >= text.Length) return text.Length;

        var searchStart = Math.Max(0, target - 100);
        for (var i = target; i >= searchStart; i--)
        {
            if (text[i] == ' ' || text[i] == '\n' || text[i] == '\r')
                return i;
        }

        return target;
    }
}
