using SmartKb.Api.Auth;
using SmartKb.Contracts;
using SmartKb.Api.Tenant;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data.Repositories;

namespace SmartKb.Api.Endpoints;

public static class RoutingEndpoints
{
    public static WebApplication MapRoutingEndpoints(this WebApplication app)
    {
        // --- Routing Rules CRUD (P1-009) ---

        app.MapGet("/api/admin/routing-rules", async (
            HttpContext httpContext,
            ITenantContextAccessor tenantAccessor,
            IRoutingRuleService ruleService) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var result = await ruleService.GetRulesAsync(tenant.TenantId, ct);
            return Results.Ok(ApiResponse<RoutingRuleListResponse>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        app.MapGet("/api/admin/routing-rules/{ruleId:guid}", async (
            HttpContext httpContext,
            Guid ruleId,
            ITenantContextAccessor tenantAccessor,
            IRoutingRuleService ruleService) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var result = await ruleService.GetRuleAsync(tenant.TenantId, ruleId, ct);
            return result is null
                ? Results.NotFound(ApiResponse<RoutingRuleDto>.Failure("Routing rule not found.", tenant.CorrelationId))
                : Results.Ok(ApiResponse<RoutingRuleDto>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        app.MapPost("/api/admin/routing-rules", async (
            HttpContext httpContext,
            CreateRoutingRuleRequest request,
            ITenantContextAccessor tenantAccessor,
            IRoutingRuleService ruleService) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var result = await ruleService.CreateRuleAsync(
                tenant.TenantId, tenant.UserId, tenant.CorrelationId, request, ct);
            return Results.Created($"/api/admin/routing-rules/{result.RuleId}",
                ApiResponse<RoutingRuleDto>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        app.MapPut("/api/admin/routing-rules/{ruleId:guid}", async (
            HttpContext httpContext,
            Guid ruleId,
            UpdateRoutingRuleRequest request,
            ITenantContextAccessor tenantAccessor,
            IRoutingRuleService ruleService) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var result = await ruleService.UpdateRuleAsync(
                tenant.TenantId, tenant.UserId, tenant.CorrelationId, ruleId, request, ct);
            return result is null
                ? Results.NotFound(ApiResponse<RoutingRuleDto>.Failure("Routing rule not found.", tenant.CorrelationId))
                : Results.Ok(ApiResponse<RoutingRuleDto>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        app.MapDelete("/api/admin/routing-rules/{ruleId:guid}", async (
            HttpContext httpContext,
            Guid ruleId,
            ITenantContextAccessor tenantAccessor,
            IRoutingRuleService ruleService) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var deleted = await ruleService.DeleteRuleAsync(
                tenant.TenantId, tenant.UserId, tenant.CorrelationId, ruleId, ct);
            return deleted
                ? Results.Ok(ApiResponse<object>.Success(new { deleted = true }, tenant.CorrelationId))
                : Results.NotFound(ApiResponse<object>.Failure("Routing rule not found.", tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        // --- Routing Analytics + Improvement (P1-009) ---

        app.MapGet("/api/admin/routing/analytics", async (
            HttpContext httpContext,
            int? windowDays,
            ITenantContextAccessor tenantAccessor,
            IRoutingAnalyticsService analyticsService) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var result = await analyticsService.GetAnalyticsAsync(tenant.TenantId, windowDays, ct);
            return Results.Ok(ApiResponse<RoutingAnalyticsSummary>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        app.MapPost("/api/admin/routing/recommendations/generate", async (
            HttpContext httpContext,
            GenerateRecommendationsRequest? request,
            ITenantContextAccessor tenantAccessor,
            IRoutingImprovementService improvementService) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var result = await improvementService.GenerateRecommendationsAsync(
                tenant.TenantId, tenant.UserId, tenant.CorrelationId,
                request?.SourceEvalReportId, ct);
            return Results.Ok(ApiResponse<RoutingRecommendationListResponse>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        app.MapGet("/api/admin/routing/recommendations", async (
            HttpContext httpContext,
            string? status,
            ITenantContextAccessor tenantAccessor,
            IRoutingImprovementService improvementService) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var result = await improvementService.GetRecommendationsAsync(tenant.TenantId, status, ct);
            return Results.Ok(ApiResponse<RoutingRecommendationListResponse>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        app.MapPost("/api/admin/routing/recommendations/{recommendationId:guid}/apply", async (
            HttpContext httpContext,
            Guid recommendationId,
            ApplyRecommendationRequest? request,
            ITenantContextAccessor tenantAccessor,
            IRoutingImprovementService improvementService) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var result = await improvementService.ApplyRecommendationAsync(
                tenant.TenantId, tenant.UserId, tenant.CorrelationId,
                recommendationId, request, ct);
            return result is null
                ? Results.NotFound(ApiResponse<RoutingRecommendationDto>.Failure("Recommendation not found or not pending.", tenant.CorrelationId))
                : Results.Ok(ApiResponse<RoutingRecommendationDto>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        app.MapPost("/api/admin/routing/recommendations/{recommendationId:guid}/dismiss", async (
            HttpContext httpContext,
            Guid recommendationId,
            ITenantContextAccessor tenantAccessor,
            IRoutingImprovementService improvementService) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var dismissed = await improvementService.DismissRecommendationAsync(
                tenant.TenantId, tenant.UserId, tenant.CorrelationId, recommendationId, ct);
            return dismissed
                ? Results.Ok(ApiResponse<object>.Success(new { dismissed = true }, tenant.CorrelationId))
                : Results.NotFound(ApiResponse<object>.Failure("Recommendation not found or not pending.", tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        app.MapGet("/api/admin/eval/reports/{reportId:guid}/recommendations", async (
            HttpContext httpContext,
            Guid reportId,
            ITenantContextAccessor tenantAccessor,
            IRoutingImprovementService improvementService) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var result = await improvementService.GetRecommendationsByEvalReportAsync(
                tenant.TenantId, reportId, ct);
            return Results.Ok(ApiResponse<RoutingRecommendationListResponse>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        return app;
    }
}
