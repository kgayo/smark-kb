using SmartKb.Api.Auth;
using SmartKb.Api.Tenant;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data.Repositories;

namespace SmartKb.Api.Endpoints;

public static class EvalEndpoints
{
    public static WebApplication MapEvalEndpoints(this WebApplication app)
    {
        // Eval report endpoints (P3-021).
        app.MapGet("/api/admin/eval/reports", async (
            ITenantContextAccessor tenantAccessor,
            HttpContext httpContext,
            string? runType,
            int? page,
            int? pageSize) =>
        {
            var tenant = tenantAccessor.Current!;
            var ct = httpContext.RequestAborted;
            var evalReportService = httpContext.RequestServices.GetRequiredService<IEvalReportService>();
            var result = await evalReportService.ListReportsAsync(
                tenant.TenantId, runType, page ?? 1, pageSize ?? 20, ct);
            return Results.Ok(ApiResponse<EvalReportListResponse>.Success(result, tenant.CorrelationId));
        }).RequirePermission("connector:manage");

        app.MapGet("/api/admin/eval/reports/{reportId:guid}", async (
            Guid reportId,
            ITenantContextAccessor tenantAccessor,
            HttpContext httpContext) =>
        {
            var tenant = tenantAccessor.Current!;
            var ct = httpContext.RequestAborted;
            var evalReportService = httpContext.RequestServices.GetRequiredService<IEvalReportService>();
            var report = await evalReportService.GetReportAsync(tenant.TenantId, reportId, ct);
            return report is not null
                ? Results.Ok(ApiResponse<EvalReportDetail>.Success(report, tenant.CorrelationId))
                : Results.NotFound(ApiResponse<object>.Failure("Eval report not found.", tenant.CorrelationId));
        }).RequirePermission("connector:manage");

        app.MapPost("/api/admin/eval/reports", async (
            PersistEvalReportRequest request,
            ITenantContextAccessor tenantAccessor,
            HttpContext httpContext) =>
        {
            var tenant = tenantAccessor.Current!;
            var ct = httpContext.RequestAborted;
            var evalReportService = httpContext.RequestServices.GetRequiredService<IEvalReportService>();
            var report = await evalReportService.PersistReportAsync(tenant.TenantId, request, tenant.UserId, ct);
            return Results.Created($"/api/admin/eval/reports/{report.Id}",
                ApiResponse<EvalReportDetail>.Success(report, tenant.CorrelationId));
        }).RequirePermission("connector:manage");

        // Gold case management endpoints (P3-022).
        app.MapGet("/api/admin/eval/gold-cases", async (
            ITenantContextAccessor tenantAccessor,
            HttpContext httpContext,
            string? tag,
            int? page,
            int? pageSize) =>
        {
            var tenant = tenantAccessor.Current!;
            var ct = httpContext.RequestAborted;
            var svc = httpContext.RequestServices.GetRequiredService<IGoldCaseService>();
            var result = await svc.ListAsync(tenant.TenantId, tag, page ?? 1, pageSize ?? 20, ct);
            return Results.Ok(ApiResponse<GoldCaseListResponse>.Success(result, tenant.CorrelationId));
        }).RequirePermission("connector:manage");

        app.MapGet("/api/admin/eval/gold-cases/{id:guid}", async (
            Guid id,
            ITenantContextAccessor tenantAccessor,
            HttpContext httpContext) =>
        {
            var tenant = tenantAccessor.Current!;
            var ct = httpContext.RequestAborted;
            var svc = httpContext.RequestServices.GetRequiredService<IGoldCaseService>();
            var detail = await svc.GetAsync(tenant.TenantId, id, ct);
            return detail is not null
                ? Results.Ok(ApiResponse<GoldCaseDetail>.Success(detail, tenant.CorrelationId))
                : Results.NotFound(ApiResponse<object>.Failure("Gold case not found.", tenant.CorrelationId));
        }).RequirePermission("connector:manage");

        app.MapPost("/api/admin/eval/gold-cases", async (
            CreateGoldCaseRequest request,
            ITenantContextAccessor tenantAccessor,
            HttpContext httpContext) =>
        {
            var tenant = tenantAccessor.Current!;
            var ct = httpContext.RequestAborted;
            var svc = httpContext.RequestServices.GetRequiredService<IGoldCaseService>();
            var detail = await svc.CreateAsync(tenant.TenantId, request, tenant.UserId, ct);
            return Results.Created($"/api/admin/eval/gold-cases/{detail.Id}",
                ApiResponse<GoldCaseDetail>.Success(detail, tenant.CorrelationId));
        }).RequirePermission("connector:manage");

        app.MapPut("/api/admin/eval/gold-cases/{id:guid}", async (
            Guid id,
            UpdateGoldCaseRequest request,
            ITenantContextAccessor tenantAccessor,
            HttpContext httpContext) =>
        {
            var tenant = tenantAccessor.Current!;
            var ct = httpContext.RequestAborted;
            var svc = httpContext.RequestServices.GetRequiredService<IGoldCaseService>();
            var detail = await svc.UpdateAsync(tenant.TenantId, id, request, tenant.UserId, ct);
            return detail is not null
                ? Results.Ok(ApiResponse<GoldCaseDetail>.Success(detail, tenant.CorrelationId))
                : Results.NotFound(ApiResponse<object>.Failure("Gold case not found.", tenant.CorrelationId));
        }).RequirePermission("connector:manage");

        app.MapDelete("/api/admin/eval/gold-cases/{id:guid}", async (
            Guid id,
            ITenantContextAccessor tenantAccessor,
            HttpContext httpContext) =>
        {
            var tenant = tenantAccessor.Current!;
            var ct = httpContext.RequestAborted;
            var svc = httpContext.RequestServices.GetRequiredService<IGoldCaseService>();
            var deleted = await svc.DeleteAsync(tenant.TenantId, id, tenant.UserId, ct);
            return deleted
                ? Results.Ok(ApiResponse<object>.Success(new { deleted = true }, tenant.CorrelationId))
                : Results.NotFound(ApiResponse<object>.Failure("Gold case not found.", tenant.CorrelationId));
        }).RequirePermission("connector:manage");

        app.MapGet("/api/admin/eval/gold-cases/export", async (
            ITenantContextAccessor tenantAccessor,
            HttpContext httpContext) =>
        {
            var tenant = tenantAccessor.Current!;
            var ct = httpContext.RequestAborted;
            var svc = httpContext.RequestServices.GetRequiredService<IGoldCaseService>();
            var jsonl = await svc.ExportAsJsonlAsync(tenant.TenantId, ct);
            return Results.Text(jsonl, "application/x-ndjson");
        }).RequirePermission("connector:manage");

        app.MapPost("/api/admin/eval/gold-cases/promote", async (
            PromoteFromFeedbackRequest request,
            ITenantContextAccessor tenantAccessor,
            HttpContext httpContext) =>
        {
            var tenant = tenantAccessor.Current!;
            var ct = httpContext.RequestAborted;
            var svc = httpContext.RequestServices.GetRequiredService<IGoldCaseService>();
            var detail = await svc.PromoteFromFeedbackAsync(tenant.TenantId, request, tenant.UserId, ct);
            return Results.Created($"/api/admin/eval/gold-cases/{detail.Id}",
                ApiResponse<GoldCaseDetail>.Success(detail, tenant.CorrelationId));
        }).RequirePermission("connector:manage");

        return app;
    }
}
