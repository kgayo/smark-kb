using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

/// <summary>
/// Splits text content into overlapping chunks respecting structural boundaries.
/// When a <see cref="SourceType"/> is provided, applies content-type-aware chunking
/// (e.g., troubleshooting-structure chunking for tickets and work items).
/// </summary>
public interface IChunkingService
{
    IReadOnlyList<TextChunk> Chunk(string text, string? title = null, ChunkingSettings? settings = null, SourceType? sourceType = null);
}
