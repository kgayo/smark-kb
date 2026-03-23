using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

public interface IRoutingImprovementService
{
    Task<RoutingRecommendationListResponse> GenerateRecommendationsAsync(
        string tenantId, string userId, string correlationId,
        Guid? sourceEvalReportId = null,
        CancellationToken ct = default);

    Task<RoutingRecommendationListResponse> GetRecommendationsAsync(
        string tenantId,
        string? status = null,
        CancellationToken ct = default);

    Task<RoutingRecommendationDto?> ApplyRecommendationAsync(
        string tenantId, string userId, string correlationId,
        Guid recommendationId,
        ApplyRecommendationRequest? overrides = null,
        CancellationToken ct = default);

    Task<bool> DismissRecommendationAsync(
        string tenantId, string userId, string correlationId,
        Guid recommendationId,
        CancellationToken ct = default);

    Task<RoutingRecommendationListResponse> GetRecommendationsByEvalReportAsync(
        string tenantId,
        Guid evalReportId,
        CancellationToken ct = default);
}
