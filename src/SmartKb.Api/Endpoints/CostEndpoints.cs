using Microsoft.AspNetCore.Mvc;
using SmartKb.Api.Auth;
using SmartKb.Contracts;
using SmartKb.Api.Tenant;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data.Repositories;

namespace SmartKb.Api.Endpoints;

public static class CostEndpoints
{
    public static WebApplication MapCostEndpoints(this WebApplication app)
    {
        // --- Cost Optimization Endpoints (P2-003) ---

        app.MapGet("/api/admin/cost-settings", async (
            HttpContext httpContext,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] ITenantCostSettingsService costSettingsService) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var result = await costSettingsService.GetSettingsAsync(tenant.TenantId, ct);
            return Results.Ok(ApiResponse<CostSettingsResponse>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        app.MapPut("/api/admin/cost-settings", async (
            HttpContext httpContext,
            UpdateCostSettingsRequest request,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] ITenantCostSettingsService costSettingsService) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var result = await costSettingsService.UpdateSettingsAsync(tenant.TenantId, request, ct);
            return Results.Ok(ApiResponse<CostSettingsResponse>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        app.MapDelete("/api/admin/cost-settings", async (
            HttpContext httpContext,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] ITenantCostSettingsService costSettingsService) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var deleted = await costSettingsService.ResetSettingsAsync(tenant.TenantId, ct);
            return deleted
                ? Results.Ok(ApiResponse<object>.Success(new { reset = true }, tenant.CorrelationId))
                : Results.NotFound(ApiResponse<object>.Failure("No tenant cost overrides found.", tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        app.MapGet("/api/admin/token-usage/summary", async (
            HttpContext httpContext,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] ITokenUsageService tokenUsageService,
            [FromQuery] int? days) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var periodDays = days ?? 30;
            var periodEnd = DateTimeOffset.UtcNow;
            var periodStart = periodEnd.AddDays(-periodDays);
            var result = await tokenUsageService.GetSummaryAsync(tenant.TenantId, periodStart, periodEnd, ct);
            return Results.Ok(ApiResponse<TokenUsageSummary>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        app.MapGet("/api/admin/token-usage/daily", async (
            HttpContext httpContext,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] ITokenUsageService tokenUsageService,
            [FromQuery] int? days) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var periodDays = days ?? 30;
            var periodEnd = DateTimeOffset.UtcNow;
            var periodStart = periodEnd.AddDays(-periodDays);
            var result = await tokenUsageService.GetDailyBreakdownAsync(tenant.TenantId, periodStart, periodEnd, ct);
            return Results.Ok(ApiResponse<IReadOnlyList<DailyUsageBreakdown>>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        app.MapGet("/api/admin/token-usage/budget-check", async (
            HttpContext httpContext,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] ITokenUsageService tokenUsageService) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var result = await tokenUsageService.CheckBudgetAsync(tenant.TenantId, ct);
            return Results.Ok(ApiResponse<BudgetCheckResult>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        return app;
    }
}
