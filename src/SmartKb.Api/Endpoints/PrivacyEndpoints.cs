using SmartKb.Api.Auth;
using SmartKb.Contracts;
using SmartKb.Api.Tenant;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data.Repositories;

namespace SmartKb.Api.Endpoints;

public static class PrivacyEndpoints
{
    public static WebApplication MapPrivacyEndpoints(this WebApplication app)
    {
        // ──── Privacy & Compliance Endpoints (P2-001) ────

        app.MapGet("/api/admin/privacy/pii-policy", async (
            HttpContext httpContext,
            ITenantContextAccessor tenantAccessor,
            IPiiPolicyService piiPolicyService) =>
        {
            var tenant = tenantAccessor.Current!;
            var ct = httpContext.RequestAborted;
            var policy = await piiPolicyService.GetPolicyAsync(tenant.TenantId, ct);
            return policy is not null
                ? Results.Ok(ApiResponse<PiiPolicyResponse>.Success(policy, tenant.CorrelationId))
                : Results.Ok(ApiResponse<object>.Success(new { message = "No custom PII policy. Using defaults (all types, redact mode)." }, tenant.CorrelationId));
        }).RequirePermission(Permissions.PrivacyManage);

        app.MapPut("/api/admin/privacy/pii-policy", async (
            HttpContext httpContext,
            PiiPolicyUpdateRequest request,
            ITenantContextAccessor tenantAccessor,
            IPiiPolicyService piiPolicyService,
            ILogger<Program> logger) =>
        {
            var tenant = tenantAccessor.Current!;
            var ct = httpContext.RequestAborted;
            try
            {
                var result = await piiPolicyService.UpsertPolicyAsync(tenant.TenantId, request, tenant.UserId, ct);
                return Results.Ok(ApiResponse<PiiPolicyResponse>.Success(result, tenant.CorrelationId));
            }
            catch (ArgumentException ex)
            {
                logger.LogWarning(ex, "PII policy upsert validation failed for tenant {TenantId}", tenant.TenantId);
                return Results.BadRequest(ApiResponse<object>.Failure(ex.Message, tenant.CorrelationId));
            }
        }).RequirePermission(Permissions.PrivacyManage);

        app.MapDelete("/api/admin/privacy/pii-policy", async (
            HttpContext httpContext,
            ITenantContextAccessor tenantAccessor,
            IPiiPolicyService piiPolicyService) =>
        {
            var tenant = tenantAccessor.Current!;
            var ct = httpContext.RequestAborted;
            var deleted = await piiPolicyService.DeletePolicyAsync(tenant.TenantId, tenant.UserId, ct);
            return deleted
                ? Results.Ok(ApiResponse<object>.Success(new { reset = true }, tenant.CorrelationId))
                : Results.NotFound(ApiResponse<object>.Failure("No custom PII policy found.", tenant.CorrelationId));
        }).RequirePermission(Permissions.PrivacyManage);

        app.MapGet("/api/admin/privacy/retention", async (
            HttpContext httpContext,
            ITenantContextAccessor tenantAccessor,
            IRetentionCleanupService retentionService) =>
        {
            var tenant = tenantAccessor.Current!;
            var ct = httpContext.RequestAborted;
            var result = await retentionService.GetPoliciesAsync(tenant.TenantId, ct);
            return Results.Ok(ApiResponse<RetentionPolicyResponse>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.PrivacyManage);

        app.MapPut("/api/admin/privacy/retention", async (
            HttpContext httpContext,
            RetentionPolicyUpdateRequest request,
            ITenantContextAccessor tenantAccessor,
            IRetentionCleanupService retentionService,
            ILogger<Program> logger) =>
        {
            var tenant = tenantAccessor.Current!;
            var ct = httpContext.RequestAborted;
            try
            {
                var result = await retentionService.UpsertPolicyAsync(tenant.TenantId, request, tenant.UserId, ct);
                return Results.Ok(ApiResponse<RetentionPolicyEntry>.Success(result, tenant.CorrelationId));
            }
            catch (ArgumentException ex)
            {
                logger.LogWarning(ex, "Retention policy upsert validation failed for tenant {TenantId}", tenant.TenantId);
                return Results.BadRequest(ApiResponse<object>.Failure(ex.Message, tenant.CorrelationId));
            }
        }).RequirePermission(Permissions.PrivacyManage);

        app.MapDelete("/api/admin/privacy/retention/{entityType}", async (
            HttpContext httpContext,
            string entityType,
            ITenantContextAccessor tenantAccessor,
            IRetentionCleanupService retentionService) =>
        {
            var tenant = tenantAccessor.Current!;
            var ct = httpContext.RequestAborted;
            var deleted = await retentionService.DeletePolicyAsync(tenant.TenantId, entityType, tenant.UserId, ct);
            return deleted
                ? Results.Ok(ApiResponse<object>.Success(new { deleted = true, entityType }, tenant.CorrelationId))
                : Results.NotFound(ApiResponse<object>.Failure($"No retention policy found for {entityType}.", tenant.CorrelationId));
        }).RequirePermission(Permissions.PrivacyManage);

        app.MapPost("/api/admin/privacy/retention/cleanup", async (
            HttpContext httpContext,
            ITenantContextAccessor tenantAccessor,
            IRetentionCleanupService retentionService) =>
        {
            var tenant = tenantAccessor.Current!;
            var ct = httpContext.RequestAborted;
            var results = await retentionService.ExecuteCleanupAsync(tenant.TenantId, ct);
            return Results.Ok(ApiResponse<IReadOnlyList<RetentionCleanupResult>>.Success(results, tenant.CorrelationId));
        }).RequirePermission(Permissions.PrivacyManage);

        app.MapPost("/api/admin/privacy/data-subject-deletion", async (
            HttpContext httpContext,
            DataSubjectDeletionRequest request,
            ITenantContextAccessor tenantAccessor,
            IDataSubjectDeletionService deletionService,
            ILogger<Program> logger) =>
        {
            var tenant = tenantAccessor.Current!;
            var ct = httpContext.RequestAborted;
            try
            {
                var result = await deletionService.RequestDeletionAsync(tenant.TenantId, request.SubjectId, tenant.UserId, ct);
                return Results.Ok(ApiResponse<DataSubjectDeletionResponse>.Success(result, tenant.CorrelationId));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Data subject deletion request failed. TenantId={TenantId}", tenant.TenantId);
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        }).RequirePermission(Permissions.PrivacyManage);

        app.MapGet("/api/admin/privacy/data-subject-deletion", async (
            HttpContext httpContext,
            ITenantContextAccessor tenantAccessor,
            IDataSubjectDeletionService deletionService) =>
        {
            var tenant = tenantAccessor.Current!;
            var ct = httpContext.RequestAborted;
            var result = await deletionService.ListDeletionRequestsAsync(tenant.TenantId, ct);
            return Results.Ok(ApiResponse<DataSubjectDeletionListResponse>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.PrivacyManage);

        app.MapGet("/api/admin/privacy/data-subject-deletion/{requestId:guid}", async (
            HttpContext httpContext,
            Guid requestId,
            ITenantContextAccessor tenantAccessor,
            IDataSubjectDeletionService deletionService) =>
        {
            var tenant = tenantAccessor.Current!;
            var ct = httpContext.RequestAborted;
            var result = await deletionService.GetDeletionRequestAsync(tenant.TenantId, requestId, ct);
            return result is not null
                ? Results.Ok(ApiResponse<DataSubjectDeletionResponse>.Success(result, tenant.CorrelationId))
                : Results.NotFound(ApiResponse<object>.Failure("Deletion request not found.", tenant.CorrelationId));
        }).RequirePermission(Permissions.PrivacyManage);

        // ──── Retention Measurable Execution Endpoints (P2-005) ────

        app.MapGet("/api/admin/privacy/retention/history", async (
            HttpContext httpContext,
            string? entityType,
            int? skip,
            int? take,
            ITenantContextAccessor tenantAccessor,
            IRetentionCleanupService retentionService) =>
        {
            var tenant = tenantAccessor.Current!;
            var ct = httpContext.RequestAborted;
            var result = await retentionService.GetExecutionHistoryAsync(
                tenant.TenantId, entityType, skip ?? 0, take ?? 50, ct);
            return Results.Ok(ApiResponse<RetentionExecutionHistoryResponse>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.PrivacyManage);

        app.MapGet("/api/admin/privacy/retention/compliance", async (
            HttpContext httpContext,
            ITenantContextAccessor tenantAccessor,
            IRetentionCleanupService retentionService) =>
        {
            var tenant = tenantAccessor.Current!;
            var ct = httpContext.RequestAborted;
            var report = await retentionService.GetComplianceReportAsync(tenant.TenantId, ct);
            return Results.Ok(ApiResponse<RetentionComplianceReport>.Success(report, tenant.CorrelationId));
        }).RequirePermission(Permissions.PrivacyManage);

        return app;
    }
}
