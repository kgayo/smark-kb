namespace SmartKb.Contracts.Models;

/// <summary>
/// A single chunk of text produced by the chunking service, before enrichment and embedding.
/// </summary>
public sealed record TextChunk(string Text, string? Context, int Index);
