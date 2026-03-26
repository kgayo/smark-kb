using Azure.Messaging.ServiceBus;
using Azure.Search.Documents.Indexes;
using SmartKb.Api.Auth;
using SmartKb.Contracts;
using SmartKb.Api.Connectors;
using SmartKb.Api.Secrets;
using SmartKb.Api.Tenant;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data.Repositories;

namespace SmartKb.Api.Endpoints;

public static class DiagnosticsEndpoints
{
    public static WebApplication MapDiagnosticsEndpoints(this WebApplication app)
    {
        // --- SLO Status Endpoint (P0-022) ---

        app.MapGet("/api/admin/slo/status", (
            ITenantContextAccessor tenantAccessor,
            SloSettings sloSettingsInstance) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            return Results.Ok(ApiResponse<object>.Success(new
            {
                targets = new
                {
                    answerLatencyP95TargetMs = sloSettingsInstance.AnswerLatencyP95TargetMs,
                    availabilityTargetPercent = sloSettingsInstance.AvailabilityTargetPercent,
                    syncLagP95TargetMinutes = sloSettingsInstance.SyncLagP95TargetMinutes,
                    noEvidenceRateThreshold = sloSettingsInstance.NoEvidenceRateThreshold,
                    deadLetterDepthThreshold = sloSettingsInstance.DeadLetterDepthThreshold,
                    rateLimitAlertThreshold = sloSettingsInstance.RateLimitAlertThreshold,
                    rateLimitAlertWindowMinutes = sloSettingsInstance.RateLimitAlertWindowMinutes,
                },
                metrics = new
                {
                    chatLatencyMetric = "smartkb.chat.latency_ms",
                    chatRequestsMetric = "smartkb.chat.requests_total",
                    chatNoEvidenceMetric = "smartkb.chat.no_evidence_total",
                    syncDurationMetric = "smartkb.ingestion.sync_duration_ms",
                    syncCompletedMetric = "smartkb.ingestion.sync_completed_total",
                    syncFailedMetric = "smartkb.ingestion.sync_failed_total",
                    deadLetterMetric = "smartkb.ingestion.dead_letter_total",
                    recordsProcessedMetric = "smartkb.ingestion.records_processed_total",
                    piiRedactionsMetric = "smartkb.security.pii_redactions_total",
                    confidenceMetric = "smartkb.chat.confidence",
                    sourceRateLimitMetric = "smartkb.ingestion.source_rate_limit_total",
                },
                dashboardHint = "Query these metrics in Azure Monitor / Application Insights customMetrics table.",
            }, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        // --- Secrets Status Endpoint ---

        app.MapGet("/api/admin/secrets/status", (
            ITenantContextAccessor tenantAccessor,
            OpenAiKeyProvider openAiKeyProvider,
            IServiceProvider sp,
            ILogger<Program> logger) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var keyVaultConfigured = sp.GetService<ISecretProvider>() is not null;

            bool openAiConfigured;
            try
            {
                var key = openAiKeyProvider.GetApiKey();
                openAiConfigured = !string.IsNullOrWhiteSpace(key);
            }
            catch (InvalidOperationException ex)
            {
                logger.LogWarning(ex, "OpenAI API key not configured for secrets status check");
                openAiConfigured = false;
            }

            return Results.Ok(new
            {
                tenantId = tenant.TenantId,
                keyVaultConfigured,
                openAiKeyConfigured = openAiConfigured,
                openAiModel = openAiKeyProvider.GetModel(),
            });
        }).RequirePermission(Permissions.ConnectorManage);

        // --- Dead-Letter Queue Inspection ---

        app.MapGet("/api/admin/ingestion/dead-letters", async (
            ITenantContextAccessor tenantAccessor,
            HttpContext httpContext) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var dlService = httpContext.RequestServices.GetService<DeadLetterService>();
            if (dlService is null)
                return Results.Ok(ApiResponse<object>.Success(
                    new { messages = Array.Empty<object>(), count = 0, serviceBusConfigured = false },
                    tenant.CorrelationId));

            var maxParam = httpContext.Request.Query["maxMessages"].FirstOrDefault();
            var maxMessages = int.TryParse(maxParam, out var m) ? m : 20;
            var result = await dlService.PeekAsync(maxMessages, ct);
            return Results.Ok(ApiResponse<DeadLetterListResponse>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        // --- Webhook Status Endpoint (P1-008) ---

        app.MapGet("/api/admin/connectors/{connectorId}/webhooks", async (
            HttpContext httpContext,
            Guid connectorId,
            ITenantContextAccessor tenantAccessor,
            IWebhookStatusService webhookStatusService) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var result = await webhookStatusService.GetByConnectorAsync(tenant.TenantId, connectorId, ct);
            return Results.Ok(ApiResponse<WebhookStatusListResponse>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        app.MapGet("/api/admin/webhooks", async (
            HttpContext httpContext,
            ITenantContextAccessor tenantAccessor,
            IWebhookStatusService webhookStatusService) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var result = await webhookStatusService.GetAllAsync(tenant.TenantId, ct);
            return Results.Ok(ApiResponse<WebhookStatusListResponse>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        // --- Diagnostics Summary Endpoint (P1-008) ---

        app.MapGet("/api/admin/diagnostics/summary", async (
            HttpContext httpContext,
            ITenantContextAccessor tenantAccessor,
            IWebhookStatusService webhookStatusService,
            OpenAiKeyProvider openAiKeyProvider,
            IServiceProvider sp) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var summary = await webhookStatusService.GetDiagnosticsSummaryAsync(tenant.TenantId, ct);

            var keyVaultConfigured = sp.GetService<ISecretProvider>() is not null;
            var logger = sp.GetRequiredService<ILogger<Program>>();
            bool openAiConfigured;
            try { openAiConfigured = !string.IsNullOrWhiteSpace(openAiKeyProvider.GetApiKey()); }
            catch (InvalidOperationException ex) { logger.LogWarning(ex, "OpenAI API key not configured for diagnostics summary"); openAiConfigured = false; }

            var searchConfigured = sp.GetService<SearchIndexClient>() is not null;
            var sbConfigured = sp.GetService<ServiceBusClient>() is not null;

            // Credential status summary (P3-009).
            int credWarn = 0, credCrit = 0, credExp = 0;
            var rotationService = sp.GetService<ISecretRotationService>();
            if (rotationService is not null)
            {
                try
                {
                    var credStatus = await rotationService.GetAllCredentialStatusesAsync(tenant.TenantId, ct);
                    credWarn = credStatus.WarningCount;
                    credCrit = credStatus.CriticalCount;
                    credExp = credStatus.ExpiredCount;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Non-fatal: diagnostics still returns even if credential check fails.
                    sp.GetRequiredService<ILogger<Program>>()
                        .LogWarning(ex, "Failed to check credential status for diagnostics summary");
                }
            }

            // Rate-limit alert summary (P3-020).
            int rateLimitAlerting = 0;
            RateLimitAlertSummary? rlAlerts = null;
            var rateLimitService = sp.GetService<IRateLimitAlertService>();
            if (rateLimitService is not null)
            {
                try
                {
                    rlAlerts = await rateLimitService.GetRateLimitAlertsAsync(tenant.TenantId, ct);
                    rateLimitAlerting = rlAlerts.TotalAlertingConnectors;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    sp.GetRequiredService<ILogger<Program>>()
                        .LogWarning(ex, "Failed to check rate-limit alerts for diagnostics summary");
                }
            }

            // Enrich connector health with rate-limit info.
            var enrichedHealth = summary.ConnectorHealth;
            if (rlAlerts is not null && rlAlerts.Alerts.Count > 0)
            {
                var alertLookup = rlAlerts.Alerts.ToDictionary(a => a.ConnectorId);
                enrichedHealth = summary.ConnectorHealth.Select(c =>
                {
                    if (alertLookup.TryGetValue(c.ConnectorId, out var alert))
                    {
                        return c with { RateLimitHits = alert.HitCount, RateLimitAlerting = true };
                    }
                    return c;
                }).ToArray();
            }

            var enriched = summary with
            {
                ServiceBusConfigured = sbConfigured,
                KeyVaultConfigured = keyVaultConfigured,
                OpenAiConfigured = openAiConfigured,
                SearchServiceConfigured = searchConfigured,
                CredentialWarnings = credWarn,
                CredentialCritical = credCrit,
                CredentialExpired = credExp,
                RateLimitAlertingConnectors = rateLimitAlerting,
                ConnectorHealth = enrichedHealth,
            };

            return Results.Ok(ApiResponse<DiagnosticsSummaryResponse>.Success(enriched, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        // --- Rate-Limit Alerts (P3-020) ---

        app.MapGet("/api/admin/diagnostics/rate-limit-alerts", async (
            HttpContext httpContext,
            ITenantContextAccessor tenantAccessor) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var rateLimitService = httpContext.RequestServices.GetRequiredService<IRateLimitAlertService>();
            var result = await rateLimitService.GetRateLimitAlertsAsync(tenant.TenantId, ct);
            return Results.Ok(ApiResponse<RateLimitAlertSummary>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        // --- Credential Status and Rotation (P3-009) ---

        app.MapGet("/api/admin/credentials/status", async (
            HttpContext httpContext,
            ITenantContextAccessor tenantAccessor) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var rotationService = httpContext.RequestServices.GetRequiredService<ISecretRotationService>();
            var result = await rotationService.GetAllCredentialStatusesAsync(tenant.TenantId, ct);
            return Results.Ok(ApiResponse<CredentialStatusSummary>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        app.MapGet("/api/admin/connectors/{connectorId:guid}/credential-status", async (
            Guid connectorId,
            HttpContext httpContext,
            ITenantContextAccessor tenantAccessor) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var rotationService = httpContext.RequestServices.GetRequiredService<ISecretRotationService>();
            var result = await rotationService.GetCredentialStatusAsync(connectorId, tenant.TenantId, ct);
            return Results.Ok(ApiResponse<ConnectorCredentialStatus>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        app.MapPost("/api/admin/connectors/{connectorId:guid}/rotate-secret", async (
            Guid connectorId,
            RotateSecretRequest request,
            HttpContext httpContext,
            ITenantContextAccessor tenantAccessor) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var rotationService = httpContext.RequestServices.GetRequiredService<ISecretRotationService>();
            var result = await rotationService.RotateSecretAsync(
                connectorId, tenant.TenantId, request.NewSecretValue, tenant.UserId ?? ResponseMessages.SystemActorId, ct);
            return result.Success
                ? Results.Ok(ApiResponse<CredentialRotationResult>.Success(result, tenant.CorrelationId))
                : Results.UnprocessableEntity(ApiResponse<CredentialRotationResult>.Failure(result.Message, tenant.CorrelationId));
        }).RequirePermission(Permissions.ConnectorManage);

        return app;
    }
}
