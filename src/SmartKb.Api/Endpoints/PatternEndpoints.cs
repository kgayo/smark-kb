using Microsoft.AspNetCore.Mvc;
using SmartKb.Api.Auth;
using SmartKb.Contracts;
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
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] ITenantRetrievalSettingsService settingsService) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var result = await settingsService.GetSettingsAsync(tenant.TenantId, ct);
            return Results.Ok(ApiResponse<RetrievalSettingsResponse>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        app.MapPut("/api/admin/retrieval-settings", async (
            HttpContext httpContext,
            UpdateRetrievalSettingsRequest request,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] ITenantRetrievalSettingsService settingsService) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var result = await settingsService.UpdateSettingsAsync(tenant.TenantId, request, ct);
            return Results.Ok(ApiResponse<RetrievalSettingsResponse>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        app.MapDelete("/api/admin/retrieval-settings", async (
            HttpContext httpContext,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] ITenantRetrievalSettingsService settingsService) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var deleted = await settingsService.ResetSettingsAsync(tenant.TenantId, ct);
            return deleted
                ? Results.Ok(ApiResponse<object>.Success(new { reset = true }, tenant.CorrelationId))
                : Results.NotFound(ApiResponse<object>.Failure(ResponseMessages.NoTenantOverrides, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        // --- Pattern Distillation Endpoints (P1-005) ---

        app.MapGet("/api/admin/patterns/candidates", async (
            HttpContext httpContext,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] IPatternDistillationService distillationService) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var result = await distillationService.FindCandidatesAsync(tenant.TenantId, ct);
            return Results.Ok(ApiResponse<DistillationCandidateListResponse>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        app.MapPost("/api/admin/patterns/distill", async (
            HttpContext httpContext,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] IPatternDistillationService distillationService) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var result = await distillationService.DistillAsync(
                tenant.TenantId, tenant.UserId, tenant.CorrelationId, ct);
            return Results.Ok(ApiResponse<DistillationResult>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        // --- Pattern Governance Endpoints (P1-006) ---

        app.MapGet("/api/patterns/governance-queue", async (
            HttpContext httpContext,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] IPatternGovernanceService governanceService,
            [FromQuery] string? trustLevel,
            [FromQuery] string? productArea,
            [FromQuery] int? page,
            [FromQuery] int? pageSize) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var result = await governanceService.GetGovernanceQueueAsync(
                tenant.TenantId, trustLevel, productArea, page ?? PaginationDefaults.DefaultPage, pageSize ?? PaginationDefaults.DefaultPageSize, ct);
            return Results.Ok(ApiResponse<PatternGovernanceQueueResponse>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.PatternApprove);

        app.MapGet("/api/patterns/{patternId}", async (
            HttpContext httpContext,
            string patternId,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] IPatternGovernanceService governanceService) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var result = await governanceService.GetPatternDetailAsync(tenant.TenantId, patternId, ct);
            return result is null
                ? Results.NotFound(ApiResponse<object>.Failure(ResponseMessages.PatternNotFound, tenant.CorrelationId))
                : Results.Ok(ApiResponse<PatternDetail>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.PatternApprove);

        app.MapPost("/api/patterns/{patternId}/review", async (
            HttpContext httpContext,
            string patternId,
            ReviewPatternRequest request,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] IPatternGovernanceService governanceService) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var result = await governanceService.ReviewPatternAsync(
                tenant.TenantId, patternId, tenant.UserId, tenant.CorrelationId, request, ct);
            return result is null
                ? Results.NotFound(ApiResponse<object>.Failure(ResponseMessages.PatternNotFound, tenant.CorrelationId))
                : Results.Ok(ApiResponse<PatternGovernanceResult>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.PatternApprove);

        app.MapPost("/api/patterns/{patternId}/approve", async (
            HttpContext httpContext,
            string patternId,
            ApprovePatternRequest request,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] IPatternGovernanceService governanceService) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var result = await governanceService.ApprovePatternAsync(
                tenant.TenantId, patternId, tenant.UserId, tenant.CorrelationId, request, ct);
            return result is null
                ? Results.NotFound(ApiResponse<object>.Failure(ResponseMessages.PatternNotFound, tenant.CorrelationId))
                : Results.Ok(ApiResponse<PatternGovernanceResult>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.PatternApprove);

        app.MapPost("/api/patterns/{patternId}/deprecate", async (
            HttpContext httpContext,
            string patternId,
            DeprecatePatternRequest request,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] IPatternGovernanceService governanceService) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var result = await governanceService.DeprecatePatternAsync(
                tenant.TenantId, patternId, tenant.UserId, tenant.CorrelationId, request, ct);
            return result is null
                ? Results.NotFound(ApiResponse<object>.Failure(ResponseMessages.PatternNotFound, tenant.CorrelationId))
                : Results.Ok(ApiResponse<PatternGovernanceResult>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.PatternDeprecate);

        // --- Pattern Version History Endpoint (P3-013) ---

        app.MapGet("/api/patterns/{patternId}/history", async (
            HttpContext httpContext,
            string patternId,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] IPatternGovernanceService governanceService) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var result = await governanceService.GetPatternHistoryAsync(
                tenant.TenantId, patternId, ct);
            return result is null
                ? Results.NotFound(ApiResponse<object>.Failure(ResponseMessages.PatternNotFound, tenant.CorrelationId))
                : Results.Ok(ApiResponse<PatternVersionHistoryResponse>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.PatternApprove);

        // --- Pattern Usage Metrics Endpoint (P3-012) ---

        app.MapGet("/api/admin/patterns/{patternId}/usage", async (
            string patternId,
            [FromServices] ITenantContextAccessor tenantAccessor,
            HttpContext httpContext) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var usageService = httpContext.RequestServices.GetRequiredService<IPatternUsageMetricsService>();
            var metrics = await usageService.GetUsageAsync(tenant.TenantId, patternId, ct);
            return Results.Ok(ApiResponse<PatternUsageMetrics>.Success(metrics, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        // --- Pattern Maintenance Endpoints (P2-004) ---

        app.MapPost("/api/admin/patterns/detect-contradictions", async (
            HttpContext httpContext,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] IContradictionDetectionService contradictionService) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var result = await contradictionService.DetectContradictionsAsync(
                tenant.TenantId, tenant.UserId, tenant.CorrelationId, httpContext.RequestAborted);
            return Results.Ok(ApiResponse<ContradictionDetectionResult>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.PatternApprove);

        app.MapGet("/api/admin/patterns/contradictions", async (
            HttpContext httpContext,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] IContradictionDetectionService contradictionService,
            [FromQuery] string? status,
            [FromQuery] int? page,
            [FromQuery] int? pageSize) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var result = await contradictionService.GetContradictionsAsync(
                tenant.TenantId, status, page ?? PaginationDefaults.DefaultPage, pageSize ?? PaginationDefaults.DefaultPageSize, httpContext.RequestAborted);
            return Results.Ok(ApiResponse<ContradictionListResponse>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.PatternApprove);

        app.MapPost("/api/admin/patterns/contradictions/{id}/resolve", async (
            Guid id,
            ResolveContradictionRequest request,
            HttpContext httpContext,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] IContradictionDetectionService contradictionService) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var result = await contradictionService.ResolveContradictionAsync(
                id, tenant.TenantId, tenant.UserId, tenant.CorrelationId, request, httpContext.RequestAborted);
            return result is null
                ? Results.NotFound(ApiResponse<object>.Failure(ResponseMessages.ContradictionNotFound, tenant.CorrelationId))
                : Results.Ok(ApiResponse<ContradictionSummary>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.PatternApprove);

        app.MapPost("/api/admin/patterns/detect-maintenance", async (
            HttpContext httpContext,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] IPatternMaintenanceService maintenanceService) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var result = await maintenanceService.DetectMaintenanceIssuesAsync(
                tenant.TenantId, tenant.UserId, tenant.CorrelationId, httpContext.RequestAborted);
            return Results.Ok(ApiResponse<MaintenanceDetectionResult>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.PatternApprove);

        app.MapGet("/api/admin/patterns/maintenance-tasks", async (
            HttpContext httpContext,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] IPatternMaintenanceService maintenanceService,
            [FromQuery] string? status,
            [FromQuery] string? taskType,
            [FromQuery] int? page,
            [FromQuery] int? pageSize) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var result = await maintenanceService.GetMaintenanceTasksAsync(
                tenant.TenantId, status, taskType, page ?? PaginationDefaults.DefaultPage, pageSize ?? PaginationDefaults.DefaultPageSize, httpContext.RequestAborted);
            return Results.Ok(ApiResponse<MaintenanceTaskListResponse>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.PatternApprove);

        app.MapPost("/api/admin/patterns/maintenance-tasks/{id}/resolve", async (
            Guid id,
            ResolveMaintenanceTaskRequest request,
            HttpContext httpContext,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] IPatternMaintenanceService maintenanceService) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var result = await maintenanceService.ResolveTaskAsync(
                id, tenant.TenantId, tenant.UserId, tenant.CorrelationId, request, httpContext.RequestAborted);
            return result is null
                ? Results.NotFound(ApiResponse<object>.Failure(ResponseMessages.MaintenanceTaskNotFound, tenant.CorrelationId))
                : Results.Ok(ApiResponse<MaintenanceTaskSummary>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.PatternApprove);

        app.MapPost("/api/admin/patterns/maintenance-tasks/{id}/dismiss", async (
            Guid id,
            ResolveMaintenanceTaskRequest request,
            HttpContext httpContext,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] IPatternMaintenanceService maintenanceService) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var result = await maintenanceService.DismissTaskAsync(
                id, tenant.TenantId, tenant.UserId, tenant.CorrelationId, request, httpContext.RequestAborted);
            return result is null
                ? Results.NotFound(ApiResponse<object>.Failure(ResponseMessages.MaintenanceTaskNotFound, tenant.CorrelationId))
                : Results.Ok(ApiResponse<MaintenanceTaskSummary>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.PatternApprove);

        return app;
    }
}
