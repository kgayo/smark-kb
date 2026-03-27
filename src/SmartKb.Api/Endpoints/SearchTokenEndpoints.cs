using Microsoft.AspNetCore.Mvc;
using SmartKb.Api.Auth;
using SmartKb.Contracts;
using SmartKb.Api.Tenant;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data.Exceptions;
using SmartKb.Data.Repositories;

namespace SmartKb.Api.Endpoints;

public static class SearchTokenEndpoints
{
    public static WebApplication MapSearchTokenEndpoints(this WebApplication app)
    {
        // --- Synonym Map Admin Endpoints (P3-004) ---

        app.MapGet("/api/admin/synonym-rules", async (
            HttpContext httpContext,
            [FromQuery] string? groupName,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] ISynonymMapService service) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var result = await service.ListAsync(tenant.TenantId, groupName, ct);
            return Results.Ok(ApiResponse<SynonymRuleListResponse>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        app.MapGet("/api/admin/synonym-rules/{ruleId:guid}", async (
            HttpContext httpContext,
            Guid ruleId,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] ISynonymMapService service) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var result = await service.GetAsync(tenant.TenantId, ruleId, ct);
            return result is null
                ? Results.NotFound(ApiResponse<object>.Failure(ResponseMessages.SynonymRuleNotFound, tenant.CorrelationId))
                : Results.Ok(ApiResponse<SynonymRuleResponse>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        app.MapPost("/api/admin/synonym-rules", async (
            HttpContext httpContext,
            CreateSynonymRuleRequest request,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] ISynonymMapService service) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var (response, validation) = await service.CreateAsync(
                tenant.TenantId, tenant.UserId, tenant.CorrelationId, request, ct);

            if (validation is not null)
                return Results.UnprocessableEntity(ApiResponse<SynonymRuleValidationResult>.Failure(
                    string.Join("; ", validation.Errors), tenant.CorrelationId));

            if (response is null)
                return Results.Problem("Unexpected null response from service.", statusCode: StatusCodes.Status500InternalServerError);

            return Results.Created($"/api/admin/synonym-rules/{response.Id}",
                ApiResponse<SynonymRuleResponse>.Success(response, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        app.MapPut("/api/admin/synonym-rules/{ruleId:guid}", async (
            HttpContext httpContext,
            Guid ruleId,
            UpdateSynonymRuleRequest request,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] ISynonymMapService service) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;

            try
            {
                var (response, validation, notFound) = await service.UpdateAsync(
                    tenant.TenantId, tenant.UserId, tenant.CorrelationId, ruleId, request, ct);

                if (notFound)
                    return Results.NotFound(ApiResponse<object>.Failure(ResponseMessages.SynonymRuleNotFound, tenant.CorrelationId));
                if (validation is not null)
                    return Results.UnprocessableEntity(ApiResponse<SynonymRuleValidationResult>.Failure(
                        string.Join("; ", validation.Errors), tenant.CorrelationId));

                if (response is null)
                    return Results.Problem("Unexpected null response from service.", statusCode: StatusCodes.Status500InternalServerError);

                return Results.Ok(ApiResponse<SynonymRuleResponse>.Success(response, tenant.CorrelationId));
            }
            catch (ConcurrencyConflictException ex)
            {
                return Results.Conflict(ApiResponse<object>.Failure(ex.Message, tenant.CorrelationId));
            }
        }).RequirePermission(Permissions.ConnectorManage);

        app.MapDelete("/api/admin/synonym-rules/{ruleId:guid}", async (
            HttpContext httpContext,
            Guid ruleId,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] ISynonymMapService service) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var deleted = await service.DeleteAsync(
                tenant.TenantId, tenant.UserId, tenant.CorrelationId, ruleId, ct);
            return deleted
                ? Results.Ok(ApiResponse<object>.Success(new { deleted = true }, tenant.CorrelationId))
                : Results.NotFound(ApiResponse<object>.Failure(ResponseMessages.SynonymRuleNotFound, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        app.MapPost("/api/admin/synonym-rules/sync", async (
            HttpContext httpContext,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] ISynonymMapService service) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var result = await service.SyncToSearchAsync(tenant.TenantId, tenant.CorrelationId, ct);
            return result.Success
                ? Results.Ok(ApiResponse<SynonymMapSyncResult>.Success(result, tenant.CorrelationId))
                : Results.UnprocessableEntity(ApiResponse<SynonymMapSyncResult>.Failure(
                    result.ErrorDetail ?? "Sync failed.", tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        app.MapPost("/api/admin/synonym-rules/seed", async (
            HttpContext httpContext,
            SeedSynonymRulesRequest request,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] ISynonymMapService service) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var count = await service.SeedDefaultsAsync(
                tenant.TenantId, tenant.UserId, tenant.CorrelationId, request.OverwriteExisting, ct);
            return Results.Ok(ApiResponse<object>.Success(new { seeded = count }, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        // --- Stop-Word Management Endpoints (P3-028) ---

        app.MapGet("/api/admin/stop-words", async (
            HttpContext httpContext,
            [FromQuery] string? groupName,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] ISearchTokenService service) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var result = await service.ListStopWordsAsync(tenant.TenantId, groupName, ct);
            return Results.Ok(ApiResponse<StopWordListResponse>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        app.MapGet("/api/admin/stop-words/{id:guid}", async (
            HttpContext httpContext,
            Guid id,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] ISearchTokenService service) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var result = await service.GetStopWordAsync(tenant.TenantId, id, ct);
            return result is null
                ? Results.NotFound(ApiResponse<object>.Failure(ResponseMessages.StopWordNotFound, tenant.CorrelationId))
                : Results.Ok(ApiResponse<StopWordResponse>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        app.MapPost("/api/admin/stop-words", async (
            HttpContext httpContext,
            CreateStopWordRequest request,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] ISearchTokenService service) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var (response, validation) = await service.CreateStopWordAsync(
                tenant.TenantId, tenant.UserId, tenant.CorrelationId, request, ct);
            if (validation is not null)
                return Results.UnprocessableEntity(ApiResponse<object>.Failure(
                    string.Join("; ", validation.Errors), tenant.CorrelationId));
            if (response is null)
                return Results.Problem("Unexpected null response from service.", statusCode: StatusCodes.Status500InternalServerError);

            return Results.Created($"/api/admin/stop-words/{response.Id}",
                ApiResponse<StopWordResponse>.Success(response, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        app.MapPut("/api/admin/stop-words/{id:guid}", async (
            HttpContext httpContext,
            Guid id,
            UpdateStopWordRequest request,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] ISearchTokenService service) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;

            try
            {
                var (response, validation, notFound) = await service.UpdateStopWordAsync(
                    tenant.TenantId, tenant.UserId, tenant.CorrelationId, id, request, ct);
                if (notFound) return Results.NotFound(ApiResponse<object>.Failure(ResponseMessages.StopWordNotFound, tenant.CorrelationId));
                if (validation is not null)
                    return Results.UnprocessableEntity(ApiResponse<object>.Failure(
                        string.Join("; ", validation.Errors), tenant.CorrelationId));
                if (response is null)
                    return Results.Problem("Unexpected null response from service.", statusCode: StatusCodes.Status500InternalServerError);

                return Results.Ok(ApiResponse<StopWordResponse>.Success(response, tenant.CorrelationId));
            }
            catch (ConcurrencyConflictException ex)
            {
                return Results.Conflict(ApiResponse<object>.Failure(ex.Message, tenant.CorrelationId));
            }
        }).RequirePermission(Permissions.ConnectorManage);

        app.MapDelete("/api/admin/stop-words/{id:guid}", async (
            HttpContext httpContext,
            Guid id,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] ISearchTokenService service) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var deleted = await service.DeleteStopWordAsync(
                tenant.TenantId, tenant.UserId, tenant.CorrelationId, id, ct);
            return deleted ? Results.NoContent() : Results.NotFound(ApiResponse<object>.Failure(ResponseMessages.StopWordNotFound, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        app.MapPost("/api/admin/stop-words/seed", async (
            HttpContext httpContext,
            SeedStopWordsRequest request,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] ISearchTokenService service) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var count = await service.SeedDefaultStopWordsAsync(
                tenant.TenantId, tenant.UserId, tenant.CorrelationId, request.OverwriteExisting, ct);
            return Results.Ok(ApiResponse<object>.Success(new { seeded = count }, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        // --- Special Token Management Endpoints (P3-028) ---

        app.MapGet("/api/admin/special-tokens", async (
            HttpContext httpContext,
            [FromQuery] string? category,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] ISearchTokenService service) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var result = await service.ListSpecialTokensAsync(tenant.TenantId, category, ct);
            return Results.Ok(ApiResponse<SpecialTokenListResponse>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        app.MapGet("/api/admin/special-tokens/{id:guid}", async (
            HttpContext httpContext,
            Guid id,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] ISearchTokenService service) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var result = await service.GetSpecialTokenAsync(tenant.TenantId, id, ct);
            return result is null
                ? Results.NotFound(ApiResponse<object>.Failure(ResponseMessages.SpecialTokenNotFound, tenant.CorrelationId))
                : Results.Ok(ApiResponse<SpecialTokenResponse>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        app.MapPost("/api/admin/special-tokens", async (
            HttpContext httpContext,
            CreateSpecialTokenRequest request,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] ISearchTokenService service) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var (response, validation) = await service.CreateSpecialTokenAsync(
                tenant.TenantId, tenant.UserId, tenant.CorrelationId, request, ct);
            if (validation is not null)
                return Results.UnprocessableEntity(ApiResponse<object>.Failure(
                    string.Join("; ", validation.Errors), tenant.CorrelationId));
            if (response is null)
                return Results.Problem("Unexpected null response from service.", statusCode: StatusCodes.Status500InternalServerError);

            return Results.Created($"/api/admin/special-tokens/{response.Id}",
                ApiResponse<SpecialTokenResponse>.Success(response, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        app.MapPut("/api/admin/special-tokens/{id:guid}", async (
            HttpContext httpContext,
            Guid id,
            UpdateSpecialTokenRequest request,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] ISearchTokenService service) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;

            try
            {
                var (response, validation, notFound) = await service.UpdateSpecialTokenAsync(
                    tenant.TenantId, tenant.UserId, tenant.CorrelationId, id, request, ct);
                if (notFound) return Results.NotFound(ApiResponse<object>.Failure(ResponseMessages.SpecialTokenNotFound, tenant.CorrelationId));
                if (validation is not null)
                    return Results.UnprocessableEntity(ApiResponse<object>.Failure(
                        string.Join("; ", validation.Errors), tenant.CorrelationId));
                if (response is null)
                    return Results.Problem("Unexpected null response from service.", statusCode: StatusCodes.Status500InternalServerError);

                return Results.Ok(ApiResponse<SpecialTokenResponse>.Success(response, tenant.CorrelationId));
            }
            catch (ConcurrencyConflictException ex)
            {
                return Results.Conflict(ApiResponse<object>.Failure(ex.Message, tenant.CorrelationId));
            }
        }).RequirePermission(Permissions.ConnectorManage);

        app.MapDelete("/api/admin/special-tokens/{id:guid}", async (
            HttpContext httpContext,
            Guid id,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] ISearchTokenService service) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var deleted = await service.DeleteSpecialTokenAsync(
                tenant.TenantId, tenant.UserId, tenant.CorrelationId, id, ct);
            return deleted ? Results.NoContent() : Results.NotFound(ApiResponse<object>.Failure(ResponseMessages.SpecialTokenNotFound, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        app.MapPost("/api/admin/special-tokens/seed", async (
            HttpContext httpContext,
            SeedSpecialTokensRequest request,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] ISearchTokenService service) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var count = await service.SeedDefaultSpecialTokensAsync(
                tenant.TenantId, tenant.UserId, tenant.CorrelationId, request.OverwriteExisting, ct);
            return Results.Ok(ApiResponse<object>.Success(new { seeded = count }, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        return app;
    }
}
