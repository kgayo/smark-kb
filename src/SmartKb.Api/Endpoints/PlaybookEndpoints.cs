using SmartKb.Api.Auth;
using SmartKb.Api.Tenant;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data.Repositories;

namespace SmartKb.Api.Endpoints;

public static class PlaybookEndpoints
{
    public static WebApplication MapPlaybookEndpoints(this WebApplication app)
    {
        app.MapGet("/api/admin/playbooks", async (
            HttpContext httpContext,
            ITenantContextAccessor tenantAccessor,
            ITeamPlaybookService playbookService) =>
        {
            var tenant = tenantAccessor.Current!;
            var ct = httpContext.RequestAborted;
            var result = await playbookService.GetPlaybooksAsync(tenant.TenantId, ct);
            return Results.Ok(ApiResponse<TeamPlaybookListResponse>.Success(result, tenant.CorrelationId));
        }).RequirePermission("connector:manage");

        app.MapGet("/api/admin/playbooks/{playbookId:guid}", async (
            HttpContext httpContext,
            Guid playbookId,
            ITenantContextAccessor tenantAccessor,
            ITeamPlaybookService playbookService) =>
        {
            var tenant = tenantAccessor.Current!;
            var ct = httpContext.RequestAborted;
            var result = await playbookService.GetPlaybookAsync(tenant.TenantId, playbookId, ct);
            return result is null
                ? Results.NotFound(ApiResponse<TeamPlaybookDto>.Failure("Playbook not found.", tenant.CorrelationId))
                : Results.Ok(ApiResponse<TeamPlaybookDto>.Success(result, tenant.CorrelationId));
        }).RequirePermission("connector:manage");

        app.MapGet("/api/admin/playbooks/team/{teamName}", async (
            HttpContext httpContext,
            string teamName,
            ITenantContextAccessor tenantAccessor,
            ITeamPlaybookService playbookService) =>
        {
            var tenant = tenantAccessor.Current!;
            var ct = httpContext.RequestAborted;
            var result = await playbookService.GetPlaybookByTeamAsync(tenant.TenantId, teamName, ct);
            return result is null
                ? Results.NotFound(ApiResponse<TeamPlaybookDto>.Failure("Playbook not found for team.", tenant.CorrelationId))
                : Results.Ok(ApiResponse<TeamPlaybookDto>.Success(result, tenant.CorrelationId));
        }).RequirePermission("connector:manage");

        app.MapPost("/api/admin/playbooks", async (
            HttpContext httpContext,
            CreateTeamPlaybookRequest request,
            ITenantContextAccessor tenantAccessor,
            ITeamPlaybookService playbookService) =>
        {
            var tenant = tenantAccessor.Current!;
            var ct = httpContext.RequestAborted;
            var result = await playbookService.CreatePlaybookAsync(
                tenant.TenantId, tenant.UserId, tenant.CorrelationId, request, ct);
            return Results.Created($"/api/admin/playbooks/{result.Id}",
                ApiResponse<TeamPlaybookDto>.Success(result, tenant.CorrelationId));
        }).RequirePermission("connector:manage");

        app.MapPut("/api/admin/playbooks/{playbookId:guid}", async (
            HttpContext httpContext,
            Guid playbookId,
            UpdateTeamPlaybookRequest request,
            ITenantContextAccessor tenantAccessor,
            ITeamPlaybookService playbookService) =>
        {
            var tenant = tenantAccessor.Current!;
            var ct = httpContext.RequestAborted;
            var result = await playbookService.UpdatePlaybookAsync(
                tenant.TenantId, tenant.UserId, tenant.CorrelationId, playbookId, request, ct);
            return result is null
                ? Results.NotFound(ApiResponse<TeamPlaybookDto>.Failure("Playbook not found.", tenant.CorrelationId))
                : Results.Ok(ApiResponse<TeamPlaybookDto>.Success(result, tenant.CorrelationId));
        }).RequirePermission("connector:manage");

        app.MapDelete("/api/admin/playbooks/{playbookId:guid}", async (
            HttpContext httpContext,
            Guid playbookId,
            ITenantContextAccessor tenantAccessor,
            ITeamPlaybookService playbookService) =>
        {
            var tenant = tenantAccessor.Current!;
            var ct = httpContext.RequestAborted;
            var deleted = await playbookService.DeletePlaybookAsync(
                tenant.TenantId, tenant.UserId, tenant.CorrelationId, playbookId, ct);
            return deleted
                ? Results.Ok(ApiResponse<object>.Success(new { deleted = true }, tenant.CorrelationId))
                : Results.NotFound(ApiResponse<object>.Failure("Playbook not found.", tenant.CorrelationId));
        }).RequirePermission("connector:manage");

        app.MapPost("/api/admin/playbooks/validate", async (
            HttpContext httpContext,
            PlaybookValidateRequest request,
            ITenantContextAccessor tenantAccessor,
            ITeamPlaybookService playbookService) =>
        {
            var tenant = tenantAccessor.Current!;
            var ct = httpContext.RequestAborted;
            var result = await playbookService.ValidateDraftAsync(
                tenant.TenantId, request.TargetTeam, request.Draft, ct);
            return Results.Ok(ApiResponse<PlaybookValidationResult>.Success(result, tenant.CorrelationId));
        }).RequirePermission("chat:query");

        return app;
    }
}
