using SmartKb.Api.Auth;
using SmartKb.Api.Tenant;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data.Repositories;

namespace SmartKb.Api.Endpoints;

public static class PatternEndpoints
{
    public static WebApplication MapPatternEndpoints(this WebApplication app)
    {
        // --- Retrieval Tuning Endpoints (P1-007) ---

        app.MapGet("/api/admin/retrieval-settings", async (
            HttpContext httpContext,
            ITenantContextAccessor tenantAccessor,
            ITenantRetrievalSettingsService settingsService) =>
        {
            var tenant = tenantAccessor.Current!;
            var ct = httpContext.RequestAborted;
            var result = await settingsService.GetSettingsAsync(tenant.TenantId, ct);
            return Results.Ok(ApiResponse<RetrievalSettingsResponse>.Success(result, tenant.CorrelationId));
        }).RequirePermission("connector:manage");

        app.MapPut("/api/admin/retrieval-settings", async (
            HttpContext httpContext,
            UpdateRetrievalSettingsRequest request,
            ITenantContextAccessor tenantAccessor,
            ITenantRetrievalSettingsService settingsService) =>
        {
            var tenant = tenantAccessor.Current!;
            var ct = httpContext.RequestAborted;
            var result = await settingsService.UpdateSettingsAsync(tenant.TenantId, request, ct);
            return Results.Ok(ApiResponse<RetrievalSettingsResponse>.Success(result, tenant.CorrelationId));
        }).RequirePermission("connector:manage");

        app.MapDelete("/api/admin/retrieval-settings", async (
            HttpContext httpContext,
            ITenantContextAccessor tenantAccessor,
            ITenantRetrievalSettingsService settingsService) =>
        {
            var tenant = tenantAccessor.Current!;
            var ct = httpContext.RequestAborted;
            var deleted = await settingsService.ResetSettingsAsync(tenant.TenantId, ct);
            return deleted
                ? Results.Ok(ApiResponse<object>.Success(new { reset = true }, tenant.CorrelationId))
                : Results.NotFound(ApiResponse<object>.Failure("No tenant overrides found.", tenant.CorrelationId));
        }).RequirePermission("connector:manage");

        // --- Pattern Distillation Endpoints (P1-005) ---

        app.MapGet("/api/admin/patterns/candidates", async (
            HttpContext httpContext,
            ITenantContextAccessor tenantAccessor,
            IPatternDistillationService distillationService) =>
        {
            var tenant = tenantAccessor.Current!;
            var ct = httpContext.RequestAborted;
            var result = await distillationService.FindCandidatesAsync(tenant.TenantId, ct);
            return Results.Ok(ApiResponse<DistillationCandidateListResponse>.Success(result, tenant.CorrelationId));
        }).RequirePermission("connector:manage");

        app.MapPost("/api/admin/patterns/distill", async (
            HttpContext httpContext,
            ITenantContextAccessor tenantAccessor,
            IPatternDistillationService distillationService) =>
        {
            var tenant = tenantAccessor.Current!;
            var ct = httpContext.RequestAborted;
            var result = await distillationService.DistillAsync(
                tenant.TenantId, tenant.UserId, tenant.CorrelationId, ct);
            return Results.Ok(ApiResponse<DistillationResult>.Success(result, tenant.CorrelationId));
        }).RequirePermission("connector:manage");

        // --- Pattern Governance Endpoints (P1-006) ---

        app.MapGet("/api/patterns/governance-queue", async (
            HttpContext httpContext,
            ITenantContextAccessor tenantAccessor,
            IPatternGovernanceService governanceService,
            string? trustLevel,
            string? productArea,
            int? page,
            int? pageSize) =>
        {
            var tenant = tenantAccessor.Current!;
            var ct = httpContext.RequestAborted;
            var result = await governanceService.GetGovernanceQueueAsync(
                tenant.TenantId, trustLevel, productArea, page ?? 1, pageSize ?? 20, ct);
            return Results.Ok(ApiResponse<PatternGovernanceQueueResponse>.Success(result, tenant.CorrelationId));
        }).RequirePermission("pattern:approve");

        app.MapGet("/api/patterns/{patternId}", async (
            HttpContext httpContext,
            string patternId,
            ITenantContextAccessor tenantAccessor,
            IPatternGovernanceService governanceService) =>
        {
            var tenant = tenantAccessor.Current!;
            var ct = httpContext.RequestAborted;
            var result = await governanceService.GetPatternDetailAsync(tenant.TenantId, patternId, ct);
            return result is null
                ? Results.NotFound()
                : Results.Ok(ApiResponse<PatternDetail>.Success(result, tenant.CorrelationId));
        }).RequirePermission("pattern:approve");

        app.MapPost("/api/patterns/{patternId}/review", async (
            HttpContext httpContext,
            string patternId,
            ReviewPatternRequest request,
            ITenantContextAccessor tenantAccessor,
            IPatternGovernanceService governanceService) =>
        {
            var tenant = tenantAccessor.Current!;
            var ct = httpContext.RequestAborted;
            var result = await governanceService.ReviewPatternAsync(
                tenant.TenantId, patternId, tenant.UserId, tenant.CorrelationId, request, ct);
            return result is null
                ? Results.NotFound()
                : Results.Ok(ApiResponse<PatternGovernanceResult>.Success(result, tenant.CorrelationId));
        }).RequirePermission("pattern:approve");

        app.MapPost("/api/patterns/{patternId}/approve", async (
            HttpContext httpContext,
            string patternId,
            ApprovePatternRequest request,
            ITenantContextAccessor tenantAccessor,
            IPatternGovernanceService governanceService) =>
        {
            var tenant = tenantAccessor.Current!;
            var ct = httpContext.RequestAborted;
            var result = await governanceService.ApprovePatternAsync(
                tenant.TenantId, patternId, tenant.UserId, tenant.CorrelationId, request, ct);
            return result is null
                ? Results.NotFound()
                : Results.Ok(ApiResponse<PatternGovernanceResult>.Success(result, tenant.CorrelationId));
        }).RequirePermission("pattern:approve");

        app.MapPost("/api/patterns/{patternId}/deprecate", async (
            HttpContext httpContext,
            string patternId,
            DeprecatePatternRequest request,
            ITenantContextAccessor tenantAccessor,
            IPatternGovernanceService governanceService) =>
        {
            var tenant = tenantAccessor.Current!;
            var ct = httpContext.RequestAborted;
            var result = await governanceService.DeprecatePatternAsync(
                tenant.TenantId, patternId, tenant.UserId, tenant.CorrelationId, request, ct);
            return result is null
                ? Results.NotFound()
                : Results.Ok(ApiResponse<PatternGovernanceResult>.Success(result, tenant.CorrelationId));
        }).RequirePermission("pattern:deprecate");

        // --- Pattern Version History Endpoint (P3-013) ---

        app.MapGet("/api/patterns/{patternId}/history", async (
            HttpContext httpContext,
            string patternId,
            ITenantContextAccessor tenantAccessor,
            IPatternGovernanceService governanceService) =>
        {
            var tenant = tenantAccessor.Current!;
            var ct = httpContext.RequestAborted;
            var result = await governanceService.GetPatternHistoryAsync(
                tenant.TenantId, patternId, ct);
            return result is null
                ? Results.NotFound()
                : Results.Ok(ApiResponse<PatternVersionHistoryResponse>.Success(result, tenant.CorrelationId));
        }).RequirePermission("pattern:approve");

        // --- Pattern Usage Metrics Endpoint (P3-012) ---

        app.MapGet("/api/admin/patterns/{patternId}/usage", async (
            string patternId,
            ITenantContextAccessor tenantAccessor,
            HttpContext httpContext) =>
        {
            var tenant = tenantAccessor.Current!;
            var ct = httpContext.RequestAborted;
            var usageService = httpContext.RequestServices.GetRequiredService<IPatternUsageMetricsService>();
            var metrics = await usageService.GetUsageAsync(tenant.TenantId, patternId, ct);
            return Results.Ok(ApiResponse<PatternUsageMetrics>.Success(metrics, tenant.CorrelationId));
        }).RequirePermission("connector:manage");

        // --- Pattern Maintenance Endpoints (P2-004) ---

        app.MapPost("/api/admin/patterns/detect-contradictions", async (
            HttpContext httpContext,
            ITenantContextAccessor tenantAccessor,
            IContradictionDetectionService contradictionService) =>
        {
            var tenant = tenantAccessor.Current!;
            var result = await contradictionService.DetectContradictionsAsync(
                tenant.TenantId, tenant.UserId, tenant.CorrelationId, httpContext.RequestAborted);
            return Results.Ok(ApiResponse<ContradictionDetectionResult>.Success(result, tenant.CorrelationId));
        }).RequirePermission("pattern:approve");

        app.MapGet("/api/admin/patterns/contradictions", async (
            HttpContext httpContext,
            ITenantContextAccessor tenantAccessor,
            IContradictionDetectionService contradictionService,
            string? status,
            int? page,
            int? pageSize) =>
        {
            var tenant = tenantAccessor.Current!;
            var result = await contradictionService.GetContradictionsAsync(
                tenant.TenantId, status, page ?? 1, pageSize ?? 20, httpContext.RequestAborted);
            return Results.Ok(ApiResponse<ContradictionListResponse>.Success(result, tenant.CorrelationId));
        }).RequirePermission("pattern:approve");

        app.MapPost("/api/admin/patterns/contradictions/{id}/resolve", async (
            Guid id,
            ResolveContradictionRequest request,
            HttpContext httpContext,
            ITenantContextAccessor tenantAccessor,
            IContradictionDetectionService contradictionService) =>
        {
            var tenant = tenantAccessor.Current!;
            var result = await contradictionService.ResolveContradictionAsync(
                id, tenant.TenantId, tenant.UserId, tenant.CorrelationId, request, httpContext.RequestAborted);
            return result is null
                ? Results.NotFound()
                : Results.Ok(ApiResponse<ContradictionSummary>.Success(result, tenant.CorrelationId));
        }).RequirePermission("pattern:approve");

        app.MapPost("/api/admin/patterns/detect-maintenance", async (
            HttpContext httpContext,
            ITenantContextAccessor tenantAccessor,
            IPatternMaintenanceService maintenanceService) =>
        {
            var tenant = tenantAccessor.Current!;
            var result = await maintenanceService.DetectMaintenanceIssuesAsync(
                tenant.TenantId, tenant.UserId, tenant.CorrelationId, httpContext.RequestAborted);
            return Results.Ok(ApiResponse<MaintenanceDetectionResult>.Success(result, tenant.CorrelationId));
        }).RequirePermission("pattern:approve");

        app.MapGet("/api/admin/patterns/maintenance-tasks", async (
            HttpContext httpContext,
            ITenantContextAccessor tenantAccessor,
            IPatternMaintenanceService maintenanceService,
            string? status,
            string? taskType,
            int? page,
            int? pageSize) =>
        {
            var tenant = tenantAccessor.Current!;
            var result = await maintenanceService.GetMaintenanceTasksAsync(
                tenant.TenantId, status, taskType, page ?? 1, pageSize ?? 20, httpContext.RequestAborted);
            return Results.Ok(ApiResponse<MaintenanceTaskListResponse>.Success(result, tenant.CorrelationId));
        }).RequirePermission("pattern:approve");

        app.MapPost("/api/admin/patterns/maintenance-tasks/{id}/resolve", async (
            Guid id,
            ResolveMaintenanceTaskRequest request,
            HttpContext httpContext,
            ITenantContextAccessor tenantAccessor,
            IPatternMaintenanceService maintenanceService) =>
        {
            var tenant = tenantAccessor.Current!;
            var result = await maintenanceService.ResolveTaskAsync(
                id, tenant.TenantId, tenant.UserId, tenant.CorrelationId, request, httpContext.RequestAborted);
            return result is null
                ? Results.NotFound()
                : Results.Ok(ApiResponse<MaintenanceTaskSummary>.Success(result, tenant.CorrelationId));
        }).RequirePermission("pattern:approve");

        app.MapPost("/api/admin/patterns/maintenance-tasks/{id}/dismiss", async (
            Guid id,
            ResolveMaintenanceTaskRequest request,
            HttpContext httpContext,
            ITenantContextAccessor tenantAccessor,
            IPatternMaintenanceService maintenanceService) =>
        {
            var tenant = tenantAccessor.Current!;
            var result = await maintenanceService.DismissTaskAsync(
                id, tenant.TenantId, tenant.UserId, tenant.CorrelationId, request, httpContext.RequestAborted);
            return result is null
                ? Results.NotFound()
                : Results.Ok(ApiResponse<MaintenanceTaskSummary>.Success(result, tenant.CorrelationId));
        }).RequirePermission("pattern:approve");

        return app;
    }
}
