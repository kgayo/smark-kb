using Microsoft.AspNetCore.Mvc;
using SmartKb.Api.Auth;
using SmartKb.Contracts;
using SmartKb.Api.Connectors;
using SmartKb.Api.Tenant;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;

namespace SmartKb.Api.Endpoints;

public static class ConnectorAdminEndpoints
{
    public static WebApplication MapConnectorAdminEndpoints(this WebApplication app)
    {
        app.MapGet("/api/admin/connectors", async (
            HttpContext httpContext,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] ConnectorAdminService service) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var result = await service.ListAsync(tenant.TenantId, ct);
            return Results.Ok(ApiResponse<ConnectorListResponse>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        app.MapPost("/api/admin/connectors", async (
            HttpContext httpContext,
            CreateConnectorRequest request,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] ConnectorAdminService service) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var (response, validation) = await service.CreateAsync(
                tenant.TenantId, tenant.UserId, tenant.CorrelationId, request, ct);

            if (validation is not null)
                return Results.UnprocessableEntity(ApiResponse<ConnectorValidationResult>.Failure(
                    string.Join("; ", validation.Errors), tenant.CorrelationId));

            return Results.Created($"/api/admin/connectors/{response!.Id}",
                ApiResponse<ConnectorResponse>.Success(response, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        app.MapGet("/api/admin/connectors/{connectorId:guid}", async (
            HttpContext httpContext,
            Guid connectorId,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] ConnectorAdminService service) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var response = await service.GetAsync(tenant.TenantId, connectorId, ct);
            return response is null
                ? Results.NotFound(ApiResponse<object>.Failure(ResponseMessages.ConnectorNotFound, tenant.CorrelationId))
                : Results.Ok(ApiResponse<ConnectorResponse>.Success(response, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        app.MapPut("/api/admin/connectors/{connectorId:guid}", async (
            HttpContext httpContext,
            Guid connectorId,
            UpdateConnectorRequest request,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] ConnectorAdminService service) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var (response, validation, notFound) = await service.UpdateAsync(
                tenant.TenantId, tenant.UserId, tenant.CorrelationId, connectorId, request, ct);

            if (notFound)
                return Results.NotFound(ApiResponse<object>.Failure(ResponseMessages.ConnectorNotFound, tenant.CorrelationId));
            if (validation is not null)
                return Results.UnprocessableEntity(ApiResponse<ConnectorValidationResult>.Failure(
                    string.Join("; ", validation.Errors), tenant.CorrelationId));

            return Results.Ok(ApiResponse<ConnectorResponse>.Success(response!, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        app.MapDelete("/api/admin/connectors/{connectorId:guid}", async (
            HttpContext httpContext,
            Guid connectorId,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] ConnectorAdminService service) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var deleted = await service.DeleteAsync(
                tenant.TenantId, tenant.UserId, tenant.CorrelationId, connectorId, ct);
            return deleted
                ? Results.Ok(ApiResponse<object>.Success(new { deleted = true }, tenant.CorrelationId))
                : Results.NotFound(ApiResponse<object>.Failure(ResponseMessages.ConnectorNotFound, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        app.MapPost("/api/admin/connectors/{connectorId:guid}/enable", async (
            HttpContext httpContext,
            Guid connectorId,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] ConnectorAdminService service) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var (found, validation, response) = await service.EnableAsync(
                tenant.TenantId, tenant.UserId, tenant.CorrelationId, connectorId, ct);

            if (!found)
                return Results.NotFound(ApiResponse<object>.Failure(ResponseMessages.ConnectorNotFound, tenant.CorrelationId));
            if (validation is not null)
                return Results.UnprocessableEntity(ApiResponse<ConnectorValidationResult>.Failure(
                    string.Join("; ", validation.Errors), tenant.CorrelationId));

            return Results.Ok(ApiResponse<ConnectorResponse>.Success(response!, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        app.MapPost("/api/admin/connectors/{connectorId:guid}/disable", async (
            HttpContext httpContext,
            Guid connectorId,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] ConnectorAdminService service) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var (found, response) = await service.DisableAsync(
                tenant.TenantId, tenant.UserId, tenant.CorrelationId, connectorId, ct);

            if (!found)
                return Results.NotFound(ApiResponse<object>.Failure(ResponseMessages.ConnectorNotFound, tenant.CorrelationId));

            return Results.Ok(ApiResponse<ConnectorResponse>.Success(response!, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        app.MapPost("/api/admin/connectors/{connectorId:guid}/test", async (
            HttpContext httpContext,
            Guid connectorId,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] ConnectorAdminService service) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var result = await service.TestConnectionAsync(
                tenant.TenantId, tenant.UserId, tenant.CorrelationId, connectorId, ct);
            return result is null
                ? Results.NotFound(ApiResponse<object>.Failure(ResponseMessages.ConnectorNotFound, tenant.CorrelationId))
                : Results.Ok(ApiResponse<TestConnectionResponse>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        app.MapPost("/api/admin/connectors/{connectorId:guid}/sync-now", async (
            HttpContext httpContext,
            Guid connectorId,
            SyncNowRequest request,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] ConnectorAdminService service) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var (syncRunId, notFound) = await service.SyncNowAsync(
                tenant.TenantId, tenant.UserId, tenant.CorrelationId, connectorId, request, ct);

            if (notFound)
                return Results.NotFound(ApiResponse<object>.Failure(ResponseMessages.ConnectorNotFound, tenant.CorrelationId));

            return Results.Accepted($"/api/admin/connectors/{connectorId}/sync-runs/{syncRunId}",
                ApiResponse<object>.Success(new { syncRunId, status = WorkflowStatus.Pending }, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorSync);

        app.MapPost("/api/admin/connectors/{connectorId:guid}/preview", async (
            HttpContext httpContext,
            Guid connectorId,
            PreviewRequest request,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] ConnectorAdminService service) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var result = await service.PreviewAsync(
                tenant.TenantId, tenant.UserId, tenant.CorrelationId, connectorId, request, ct);
            return result is null
                ? Results.NotFound(ApiResponse<object>.Failure(ResponseMessages.ConnectorNotFound, tenant.CorrelationId))
                : Results.Ok(ApiResponse<PreviewResponse>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        app.MapPost("/api/admin/connectors/{connectorId:guid}/validate-mapping", (
            Guid connectorId,
            FieldMappingConfig mapping,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] ConnectorAdminService service) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var result = service.ValidateFieldMapping(mapping);
            return Results.Ok(ApiResponse<ConnectorValidationResult>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        app.MapPost("/api/admin/connectors/{connectorId:guid}/preview-retrieval", async (
            HttpContext httpContext,
            Guid connectorId,
            PreviewRetrievalRequest request,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] ConnectorAdminService service) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var result = await service.PreviewRetrievalAsync(
                tenant.TenantId, tenant.UserId, tenant.CorrelationId, connectorId, request, ct);
            return result is null
                ? Results.NotFound(ApiResponse<object>.Failure(ResponseMessages.ConnectorNotFound, tenant.CorrelationId))
                : Results.Ok(ApiResponse<PreviewRetrievalResponse>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        app.MapGet("/api/admin/connectors/{connectorId:guid}/sync-runs", async (
            HttpContext httpContext,
            Guid connectorId,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] ConnectorAdminService service) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var result = await service.ListSyncRunsAsync(tenant.TenantId, connectorId, ct);
            return result is null
                ? Results.NotFound(ApiResponse<object>.Failure(ResponseMessages.ConnectorNotFound, tenant.CorrelationId))
                : Results.Ok(ApiResponse<SyncRunListResponse>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        app.MapGet("/api/admin/connectors/{connectorId:guid}/sync-runs/{syncRunId:guid}", async (
            HttpContext httpContext,
            Guid connectorId,
            Guid syncRunId,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] ConnectorAdminService service) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var result = await service.GetSyncRunAsync(tenant.TenantId, connectorId, syncRunId, ct);
            return result is null
                ? Results.NotFound(ApiResponse<object>.Failure(ResponseMessages.SyncRunNotFound, tenant.CorrelationId))
                : Results.Ok(ApiResponse<SyncRunSummary>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        // --- OAuth Endpoints (P3-019) ---

        app.MapGet("/api/admin/connectors/{connectorId:guid}/oauth/authorize", async (
            HttpContext httpContext,
            Guid connectorId,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] ConnectorAdminService service) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var result = await service.GetOAuthAuthorizeUrlAsync(tenant.TenantId, connectorId, ct);
            if (result is null)
                return Results.NotFound(ApiResponse<object>.Failure(
                    "Connector not found, not configured for OAuth, or OAuth is not enabled.", tenant.CorrelationId));

            return Results.Ok(ApiResponse<OAuthAuthorizeUrlResponse>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        app.MapGet("/api/admin/connectors/{connectorId:guid}/oauth/callback", async (
            HttpContext httpContext,
            Guid connectorId,
            [FromQuery] string code,
            [FromQuery] string state,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] ConnectorAdminService service) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var (response, notFound, invalidState) = await service.HandleOAuthCallbackAsync(
                tenant.TenantId, tenant.UserId, tenant.CorrelationId, connectorId, code, state, ct);

            if (notFound)
                return Results.NotFound(ApiResponse<object>.Failure(ResponseMessages.ConnectorNotFound, tenant.CorrelationId));
            if (invalidState)
                return Results.BadRequest(ApiResponse<object>.Failure("Invalid or expired state parameter.", tenant.CorrelationId));

            return Results.Ok(ApiResponse<OAuthCallbackResponse>.Success(response!, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        return app;
    }
}
