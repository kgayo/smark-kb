using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

/// <summary>
/// Persists and queries eval report history for trend reporting (P3-021).
/// </summary>
public interface IEvalReportService
{
    /// <summary>Persist an eval report after a harness run.</summary>
    Task<EvalReportDetail> PersistReportAsync(string tenantId, PersistEvalReportRequest request, string actorId, CancellationToken ct = default);

    /// <summary>List eval reports for a tenant with pagination and optional run type filter.</summary>
    Task<EvalReportListResponse> ListReportsAsync(string tenantId, string? runType = null, int page = 1, int pageSize = PaginationDefaults.DefaultPageSize, CancellationToken ct = default);

    /// <summary>Get a single eval report by ID.</summary>
    Task<EvalReportDetail?> GetReportAsync(string tenantId, Guid reportId, CancellationToken ct = default);
}
