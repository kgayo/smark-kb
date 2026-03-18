using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

/// <summary>
/// Pre-retrieval query classification service (P3-001, FR-TRIAGE-001, FR-TRIAGE-002).
/// Classifies support inquiries before retrieval to bias search filters and improve routing.
/// </summary>
public interface IQueryClassificationService
{
    /// <summary>
    /// Classifies a support query into issue category, product area, severity, and identifies
    /// missing information that would improve answer quality.
    /// </summary>
    Task<ClassificationResult> ClassifyAsync(
        string query,
        IReadOnlyList<ChatMessage>? sessionHistory = null,
        CancellationToken cancellationToken = default);
}
