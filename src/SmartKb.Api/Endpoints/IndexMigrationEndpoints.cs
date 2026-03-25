using SmartKb.Api.Auth;
using SmartKb.Api.Tenant;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data.Repositories;

namespace SmartKb.Api.Endpoints;

public static class IndexMigrationEndpoints
{
    public static WebApplication MapIndexMigrationEndpoints(this WebApplication app)
    {
        // --- Index Migration Admin Endpoints (P3-005) ---
        // Service resolved from DI at request time; returns 503 when SearchService is not configured.

        app.MapGet("/api/admin/index-migrations/{indexType}/current", async (
            string indexType,
            ITenantContextAccessor tenantAccessor,
            HttpContext httpContext) =>
        {
            var tenant = tenantAccessor.Current!;
            var ct = httpContext.RequestAborted;
            var service = httpContext.RequestServices.GetService<IIndexMigrationService>();
            if (service is null)
                return Results.Json(ApiResponse<object>.Failure(ResponseMessages.SearchServiceNotConfigured, tenant.CorrelationId), statusCode: 503);
            var result = await service.GetCurrentVersionAsync(indexType, ct);
            return result is null
                ? Results.NotFound(ApiResponse<object>.Failure($"No version tracked for index type '{indexType}'.", tenant.CorrelationId))
                : Results.Ok(ApiResponse<IndexSchemaVersionInfo>.Success(result, tenant.CorrelationId));
        }).RequirePermission("connector:manage");

        app.MapGet("/api/admin/index-migrations/{indexType}/versions", async (
            string indexType,
            ITenantContextAccessor tenantAccessor,
            HttpContext httpContext) =>
        {
            var tenant = tenantAccessor.Current!;
            var ct = httpContext.RequestAborted;
            var service = httpContext.RequestServices.GetService<IIndexMigrationService>();
            if (service is null)
                return Results.Json(ApiResponse<object>.Failure(ResponseMessages.SearchServiceNotConfigured, tenant.CorrelationId), statusCode: 503);
            var result = await service.ListVersionsAsync(indexType, ct);
            return Results.Ok(ApiResponse<IReadOnlyList<IndexSchemaVersionInfo>>.Success(result, tenant.CorrelationId));
        }).RequirePermission("connector:manage");

        app.MapGet("/api/admin/index-migrations/{indexType}/plan", async (
            string indexType,
            ITenantContextAccessor tenantAccessor,
            HttpContext httpContext) =>
        {
            var tenant = tenantAccessor.Current!;
            var ct = httpContext.RequestAborted;
            var service = httpContext.RequestServices.GetService<IIndexMigrationService>();
            if (service is null)
                return Results.Json(ApiResponse<object>.Failure(ResponseMessages.SearchServiceNotConfigured, tenant.CorrelationId), statusCode: 503);
            var result = await service.PlanMigrationAsync(indexType, ct);
            return Results.Ok(ApiResponse<MigrationPlan>.Success(result, tenant.CorrelationId));
        }).RequirePermission("connector:manage");

        app.MapPost("/api/admin/index-migrations/{indexType}/execute", async (
            string indexType,
            ITenantContextAccessor tenantAccessor,
            HttpContext httpContext) =>
        {
            var tenant = tenantAccessor.Current!;
            var ct = httpContext.RequestAborted;
            var service = httpContext.RequestServices.GetService<IIndexMigrationService>();
            if (service is null)
                return Results.Json(ApiResponse<object>.Failure(ResponseMessages.SearchServiceNotConfigured, tenant.CorrelationId), statusCode: 503);
            var result = await service.ExecuteMigrationAsync(indexType, tenant.UserId, ct);
            return result.Success
                ? Results.Ok(ApiResponse<MigrationResult>.Success(result, tenant.CorrelationId))
                : Results.UnprocessableEntity(ApiResponse<MigrationResult>.Failure(
                    result.Error ?? "Migration failed.", tenant.CorrelationId));
        }).RequirePermission("connector:manage");

        app.MapPost("/api/admin/index-migrations/{indexType}/rollback", async (
            string indexType,
            ITenantContextAccessor tenantAccessor,
            HttpContext httpContext) =>
        {
            var tenant = tenantAccessor.Current!;
            var ct = httpContext.RequestAborted;
            var service = httpContext.RequestServices.GetService<IIndexMigrationService>();
            if (service is null)
                return Results.Json(ApiResponse<object>.Failure(ResponseMessages.SearchServiceNotConfigured, tenant.CorrelationId), statusCode: 503);
            var result = await service.RollbackAsync(indexType, tenant.UserId, ct);
            return result.Success
                ? Results.Ok(ApiResponse<MigrationResult>.Success(result, tenant.CorrelationId))
                : Results.UnprocessableEntity(ApiResponse<MigrationResult>.Failure(
                    result.Error ?? "Rollback failed.", tenant.CorrelationId));
        }).RequirePermission("connector:manage");

        app.MapPost("/api/admin/index-migrations/{indexType}/bootstrap", async (
            string indexType,
            ITenantContextAccessor tenantAccessor,
            HttpContext httpContext) =>
        {
            var tenant = tenantAccessor.Current!;
            var ct = httpContext.RequestAborted;
            var service = httpContext.RequestServices.GetService<IIndexMigrationService>();
            if (service is null)
                return Results.Json(ApiResponse<object>.Failure(ResponseMessages.SearchServiceNotConfigured, tenant.CorrelationId), statusCode: 503);
            var result = await service.EnsureVersionTrackingAsync(indexType, tenant.UserId, ct);
            return Results.Ok(ApiResponse<IndexSchemaVersionInfo>.Success(result, tenant.CorrelationId));
        }).RequirePermission("connector:manage");

        app.MapDelete("/api/admin/index-migrations/retired/{versionId:guid}", async (
            Guid versionId,
            ITenantContextAccessor tenantAccessor,
            HttpContext httpContext) =>
        {
            var tenant = tenantAccessor.Current!;
            var ct = httpContext.RequestAborted;
            var service = httpContext.RequestServices.GetService<IIndexMigrationService>();
            if (service is null)
                return Results.Json(ApiResponse<object>.Failure(ResponseMessages.SearchServiceNotConfigured, tenant.CorrelationId), statusCode: 503);
            var deleted = await service.DeleteRetiredVersionAsync(versionId, tenant.UserId, ct);
            return deleted
                ? Results.Ok(ApiResponse<object>.Success(new { deleted = true }, tenant.CorrelationId))
                : Results.NotFound(ApiResponse<object>.Failure("Retired version not found or not eligible for deletion.", tenant.CorrelationId));
        }).RequirePermission("connector:manage");

        return app;
    }
}
