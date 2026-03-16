using Microsoft.Extensions.Logging;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

/// <summary>
/// Normalizes canonical records into enriched evidence chunks.
/// Pipeline: CanonicalRecord -> Chunking -> Enrichment -> EvidenceChunks with lineage IDs.
/// </summary>
public sealed class NormalizationPipeline : INormalizationPipeline
{
    private readonly IChunkingService _chunker;
    private readonly IEnrichmentService _enricher;
    private readonly ChunkingSettings _settings;
    private readonly ILogger<NormalizationPipeline> _logger;

    public NormalizationPipeline(
        IChunkingService chunker,
        IEnrichmentService enricher,
        ChunkingSettings settings,
        ILogger<NormalizationPipeline> logger)
    {
        _chunker = chunker;
        _enricher = enricher;
        _settings = settings;
        _logger = logger;
    }

    public IReadOnlyList<EvidenceChunk> Process(CanonicalRecord record)
    {
        // 1. Enrich the record to extract metadata.
        var enrichment = _enricher.Enrich(record);

        // 2. Chunk the text content.
        var textChunks = _chunker.Chunk(record.TextContent, record.Title, _settings);

        // 3. Map each text chunk to an EvidenceChunk with full lineage and metadata.
        var chunks = new List<EvidenceChunk>(textChunks.Count);
        foreach (var tc in textChunks)
        {
            chunks.Add(new EvidenceChunk
            {
                ChunkId = $"{record.EvidenceId}_chunk_{tc.Index}",
                EvidenceId = record.EvidenceId,
                TenantId = record.TenantId,
                ChunkIndex = tc.Index,
                ChunkText = tc.Text,
                ChunkContext = tc.Context,
                EmbeddingVector = null, // Embedding deferred to indexing pipeline (P0-011).
                SourceSystem = record.SourceSystem,
                SourceType = record.SourceType,
                Status = record.Status,
                UpdatedAt = record.UpdatedAt,
                ProductArea = enrichment.ProductArea ?? record.ProductArea,
                Tags = record.Tags,
                Visibility = record.Permissions.Visibility,
                AllowedGroups = record.Permissions.AllowedGroups,
                AccessLabel = record.AccessLabel,
                Title = record.Title,
                SourceUrl = record.SourceLocator.Url,
                EnrichmentVersion = enrichment.EnrichmentVersion,
                ErrorTokens = enrichment.ErrorTokens,
            });
        }

        _logger.LogDebug(
            "Normalized {EvidenceId}: {ChunkCount} chunks, category={Category}, severity={Severity}, pii=[{PiiFlags}]",
            record.EvidenceId, chunks.Count, enrichment.Category, enrichment.Severity,
            string.Join(",", enrichment.PiiFlags));

        return chunks;
    }

    public IReadOnlyList<EvidenceChunk> ProcessBatch(IReadOnlyList<CanonicalRecord> records)
    {
        var allChunks = new List<EvidenceChunk>();
        foreach (var record in records)
        {
            try
            {
                allChunks.AddRange(Process(record));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to normalize record {EvidenceId} for tenant {TenantId}. Skipping.",
                    record.EvidenceId, record.TenantId);
            }
        }
        return allChunks;
    }
}
