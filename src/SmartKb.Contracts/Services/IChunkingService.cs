using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

/// <summary>
/// Splits text content into overlapping chunks respecting structural boundaries.
/// </summary>
public interface IChunkingService
{
    IReadOnlyList<TextChunk> Chunk(string text, string? title = null, ChunkingSettings? settings = null);
}
