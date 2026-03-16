using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

/// <summary>
/// Normalizes canonical records into enriched evidence chunks ready for indexing.
/// Orchestrates chunking and baseline enrichment in sequence.
/// </summary>
public interface INormalizationPipeline
{
    IReadOnlyList<EvidenceChunk> Process(CanonicalRecord record);
    IReadOnlyList<EvidenceChunk> ProcessBatch(IReadOnlyList<CanonicalRecord> records);
}
