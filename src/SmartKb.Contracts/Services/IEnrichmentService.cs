using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

/// <summary>
/// Extracts enrichment metadata from a canonical record: category, severity,
/// environment, error tokens, and baseline PII detection.
/// </summary>
public interface IEnrichmentService
{
    EnrichmentResult Enrich(CanonicalRecord record);
}
