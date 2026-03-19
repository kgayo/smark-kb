using System.Reflection;
using Azure;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Azure.Search.Documents.Indexes;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SmartKb.Api.Audit;
using SmartKb.Api.Auth;
using SmartKb.Api.Connectors;
using SmartKb.Api.Secrets;
using SmartKb.Api.Tenant;
using SmartKb.Api.Webhooks;
using SmartKb.Contracts;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data;
using SmartKb.Data.Repositories;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();

// --- OpenTelemetry ---
var otelServiceName = "SmartKb.Api";
var appInsightsConnStr = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];

var otelBuilder = builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(otelServiceName))
    .WithTracing(t => t
        .AddSource(Diagnostics.ApiSourceName)
        .AddSource(Diagnostics.OrchestrationSourceName)
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSqlClientInstrumentation())
    .WithMetrics(m => m
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddMeter(Diagnostics.MeterName));

if (!string.IsNullOrEmpty(appInsightsConnStr))
{
    otelBuilder.UseAzureMonitor(o => o.ConnectionString = appInsightsConnStr);
}

builder.Logging.AddOpenTelemetry(o =>
{
    o.IncludeScopes = true;
    o.IncludeFormattedMessage = true;
});

if (!string.IsNullOrEmpty(builder.Configuration["AzureAd:ClientId"])
    && !builder.Configuration["AzureAd:ClientId"]!.StartsWith('<'))
{
    builder.Services.AddMicrosoftIdentityWebApiAuthentication(builder.Configuration);
}
else
{
    builder.Services.AddAuthentication();
}

builder.Services.AddAuthorizationBuilder()
    .AddPermissionPolicies()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());

builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddScoped<ITenantContextAccessor, TenantContextAccessor>();
builder.Services.AddSecretArchitecture(builder.Configuration);

// Service Bus registration — prefer Managed Identity via FullyQualifiedNamespace; fall back to connection string.
var serviceBusSettings = new ServiceBusSettings();
builder.Configuration.GetSection(ServiceBusSettings.SectionName).Bind(serviceBusSettings);
builder.Services.AddSingleton(serviceBusSettings);

if (serviceBusSettings.IsConfigured)
{
    var sbClient = serviceBusSettings.UsesManagedIdentity
        ? new ServiceBusClient(serviceBusSettings.FullyQualifiedNamespace, new DefaultAzureCredential())
        : new ServiceBusClient(serviceBusSettings.ConnectionString);

    builder.Services.AddSingleton(sbClient);
    builder.Services.AddSingleton<ISyncJobPublisher, ServiceBusSyncJobPublisher>();
    builder.Services.AddSingleton<DeadLetterService>();
}
else
{
    builder.Services.AddSingleton<ISyncJobPublisher, InMemorySyncJobPublisher>();
}

// Webhook settings.
var webhookSettings = new WebhookSettings();
builder.Configuration.GetSection(WebhookSettings.SectionName).Bind(webhookSettings);
builder.Services.AddSingleton(webhookSettings);

// OAuth settings (P3-019).
var oauthSettings = new OAuthSettings();
builder.Configuration.GetSection("OAuth").Bind(oauthSettings);
builder.Services.AddSingleton(oauthSettings);

builder.Services.AddHttpClient("oauth");

if (oauthSettings.IsConfigured)
{
    builder.Services.AddSingleton<IOAuthTokenService, OAuthTokenService>();
}

// Normalization pipeline (chunking + enrichment).
var chunkingSettings = new SmartKb.Contracts.Configuration.ChunkingSettings();
builder.Configuration.GetSection(SmartKb.Contracts.Configuration.ChunkingSettings.SectionName).Bind(chunkingSettings);
builder.Services.AddSingleton(chunkingSettings);
builder.Services.AddSingleton<IChunkingService, SmartKb.Contracts.Services.TextChunkingService>();
builder.Services.AddSingleton<IEnrichmentService, SmartKb.Contracts.Services.EnhancedEnrichmentService>();
builder.Services.AddSingleton<INormalizationPipeline, SmartKb.Contracts.Services.NormalizationPipeline>();

// Text extraction service for binary documents (PDF, DOCX, PPTX, XLSX).
builder.Services.AddSingleton<ITextExtractionService, SmartKb.Contracts.Services.TextExtractionService>();

// Connector clients — register all IConnectorClient implementations.
builder.Services.AddHttpClient("AzureDevOps");
builder.Services.AddHttpClient("SharePoint");
builder.Services.AddHttpClient("HubSpot");
builder.Services.AddHttpClient("ClickUp");
builder.Services.AddSingleton<SmartKb.Contracts.Connectors.AzureDevOpsConnectorClient>();
builder.Services.AddSingleton<IConnectorClient>(sp => sp.GetRequiredService<SmartKb.Contracts.Connectors.AzureDevOpsConnectorClient>());
builder.Services.AddSingleton<IEscalationTargetConnector>(sp => sp.GetRequiredService<SmartKb.Contracts.Connectors.AzureDevOpsConnectorClient>());
builder.Services.AddSingleton<IConnectorClient, SmartKb.Contracts.Connectors.SharePointConnectorClient>();
builder.Services.AddSingleton<IConnectorClient, SmartKb.Contracts.Connectors.HubSpotConnectorClient>();
builder.Services.AddSingleton<SmartKb.Contracts.Connectors.ClickUpConnectorClient>();
builder.Services.AddSingleton<IConnectorClient>(sp => sp.GetRequiredService<SmartKb.Contracts.Connectors.ClickUpConnectorClient>());
builder.Services.AddSingleton<IEscalationTargetConnector>(sp => sp.GetRequiredService<SmartKb.Contracts.Connectors.ClickUpConnectorClient>());

// Webhook managers — register all IWebhookManager implementations.
builder.Services.AddSingleton<IWebhookManager, SmartKb.Contracts.Connectors.AdoWebhookManager>();
builder.Services.AddSingleton<IWebhookManager, SmartKb.Contracts.Connectors.SharePointWebhookManager>();
builder.Services.AddSingleton<IWebhookManager, SmartKb.Contracts.Connectors.HubSpotWebhookManager>();
builder.Services.AddSingleton<IWebhookManager, SmartKb.Contracts.Connectors.ClickUpWebhookManager>();

// Azure AI Search — prefer Managed Identity via Endpoint; fall back to admin API key.
var searchSettings = new SearchServiceSettings();
builder.Configuration.GetSection(SearchServiceSettings.SectionName).Bind(searchSettings);
builder.Services.AddSingleton(searchSettings);

var retrievalSettings = new RetrievalSettings();
builder.Configuration.GetSection(RetrievalSettings.SectionName).Bind(retrievalSettings);
builder.Services.AddSingleton(retrievalSettings);

if (searchSettings.IsConfigured)
{
    var searchIndexClient = searchSettings.UsesManagedIdentity
        ? new SearchIndexClient(new Uri(searchSettings.Endpoint), new DefaultAzureCredential())
        : new SearchIndexClient(new Uri(searchSettings.Endpoint), new AzureKeyCredential(searchSettings.AdminApiKey));

    builder.Services.AddSingleton(searchIndexClient);
    builder.Services.AddSingleton<AzureSearchIndexingService>();
    builder.Services.AddSingleton<IIndexingService>(sp => sp.GetRequiredService<AzureSearchIndexingService>());
    builder.Services.AddSingleton<AzureSearchPatternIndexingService>();
    builder.Services.AddSingleton<IPatternIndexingService>(sp => sp.GetRequiredService<AzureSearchPatternIndexingService>());

    // P1-004: Use FusedRetrievalService (Evidence + Pattern) when fusion is enabled; fall back to evidence-only.
    if (retrievalSettings.EnablePatternFusion)
        builder.Services.AddSingleton<IRetrievalService, FusedRetrievalService>();
    else
        builder.Services.AddSingleton<IRetrievalService, AzureSearchRetrievalService>();

    // P3-005: Index migration service requires SearchIndexClient and concrete indexing services.
    builder.Services.AddScoped<IIndexMigrationService, IndexMigrationService>();
}

// SLO settings (P0-022).
var sloSettings = new SloSettings();
builder.Configuration.GetSection(SloSettings.SectionName).Bind(sloSettings);
builder.Services.AddSingleton(sloSettings);

// Session settings.
var sessionSettings = new SessionSettings();
builder.Configuration.GetSection(SessionSettings.SectionName).Bind(sessionSettings);
builder.Services.AddSingleton(sessionSettings);

// Escalation settings (D-004).
var escalationSettings = new EscalationSettings();
builder.Configuration.GetSection(EscalationSettings.SectionName).Bind(escalationSettings);
builder.Services.AddSingleton(escalationSettings);

// Distillation settings (P1-005 / D-008).
var distillationSettings = new DistillationSettings();
builder.Configuration.GetSection(DistillationSettings.SectionName).Bind(distillationSettings);
builder.Services.AddSingleton(distillationSettings);

// Case-card quality settings (P1-011).
var caseCardQualitySettings = new SmartKb.Contracts.Configuration.CaseCardQualitySettings();
builder.Configuration.GetSection(SmartKb.Contracts.Configuration.CaseCardQualitySettings.SectionName).Bind(caseCardQualitySettings);
builder.Services.AddSingleton(caseCardQualitySettings);
builder.Services.AddSingleton<SmartKb.Contracts.Services.ICaseCardQualityValidator, SmartKb.Contracts.Services.CaseCardQualityValidator>();

// Routing analytics settings (P1-009).
var routingAnalyticsSettings = new RoutingAnalyticsSettings();
builder.Configuration.GetSection(RoutingAnalyticsSettings.SectionName).Bind(routingAnalyticsSettings);
builder.Services.AddSingleton(routingAnalyticsSettings);

// Chat orchestration — OpenAI embedding + chat with structured outputs.
// Requires both OpenAI API key and search service to be configured.
var chatOrchestrationSettings = new ChatOrchestrationSettings();
builder.Configuration.GetSection(ChatOrchestrationSettings.SectionName).Bind(chatOrchestrationSettings);
builder.Services.AddSingleton(chatOrchestrationSettings);

var openAiSettings = new OpenAiSettings();
builder.Configuration.GetSection(OpenAiSettings.SectionName).Bind(openAiSettings);
builder.Services.AddSingleton(openAiSettings);

var embeddingSettings = new EmbeddingSettings();
builder.Configuration.GetSection(EmbeddingSettings.SectionName).Bind(embeddingSettings);
builder.Services.AddSingleton(embeddingSettings);

// Cost optimization settings (P2-003).
var costOptimizationSettings = new CostOptimizationSettings();
builder.Configuration.GetSection(CostOptimizationSettings.SectionName).Bind(costOptimizationSettings);
builder.Services.AddSingleton(costOptimizationSettings);

// Pattern maintenance settings (P2-004).
var patternMaintenanceSettings = new PatternMaintenanceSettings();
builder.Configuration.GetSection(PatternMaintenanceSettings.SectionName).Bind(patternMaintenanceSettings);
builder.Services.AddSingleton(patternMaintenanceSettings);

// Retention settings (P2-005).
builder.Services.Configure<RetentionSettings>(
    builder.Configuration.GetSection(RetentionSettings.SectionName));

// Eval notification settings (P3-007).
var evalNotificationSettings = new EvalNotificationSettings();
builder.Configuration.GetSection(EvalNotificationSettings.SectionName).Bind(evalNotificationSettings);
builder.Services.AddSingleton(evalNotificationSettings);

if (evalNotificationSettings.IsConfigured)
{
    builder.Services.AddHttpClient("EvalNotification");
    builder.Services.AddSingleton<IEvalNotificationService, WebhookEvalNotificationClient>();
}

builder.Services.AddHttpClient("OpenAi");

builder.Services.AddSingleton<IPiiRedactionService, PiiRedactionService>();

if (!string.IsNullOrEmpty(openAiSettings.ApiKey) && searchSettings.IsConfigured)
{
    builder.Services.AddSingleton<IEmbeddingService, OpenAiEmbeddingService>();
    builder.Services.AddSingleton<IQueryClassificationService, OpenAiQueryClassificationService>();
    builder.Services.AddSingleton<ISessionSummarizationService, OpenAiSessionSummarizationService>();
    builder.Services.AddScoped<IChatOrchestrator, SmartKb.Contracts.Services.ChatOrchestrator>();
}

// Blob Storage — prefer Managed Identity via ServiceUri; fall back to connection string.
var blobSettings = new BlobStorageSettings();
builder.Configuration.GetSection(BlobStorageSettings.SectionName).Bind(blobSettings);
builder.Services.AddSingleton(blobSettings);

if (blobSettings.IsConfigured)
{
    var containerClient = blobSettings.UsesManagedIdentity
        ? new BlobServiceClient(new Uri(blobSettings.ServiceUri), new DefaultAzureCredential())
            .GetBlobContainerClient(blobSettings.RawContentContainer)
        : new BlobServiceClient(blobSettings.ConnectionString)
            .GetBlobContainerClient(blobSettings.RawContentContainer);

    builder.Services.AddSingleton(containerClient);
    builder.Services.AddSingleton<IBlobStorageService, AzureBlobStorageService>();
}

var connectionString = builder.Configuration.GetConnectionString("SmartKbDb");
if (!string.IsNullOrEmpty(connectionString))
{
    builder.Services.AddSmartKbData(connectionString);
    builder.Services.AddScoped<ConnectorAdminService>();
    builder.Services.AddScoped<ISynonymMapService>(sp =>
        new SmartKb.Data.Repositories.SynonymMapService(
            sp.GetRequiredService<SmartKbDbContext>(),
            sp.GetRequiredService<IAuditEventWriter>(),
            sp.GetRequiredService<SearchServiceSettings>(),
            sp.GetRequiredService<ILogger<SmartKb.Data.Repositories.SynonymMapService>>(),
            sp.GetService<SearchIndexClient>()));
    builder.Services.AddScoped<AdoWebhookHandler>();
    builder.Services.AddScoped<SharePointWebhookHandler>();
    builder.Services.AddScoped<HubSpotWebhookHandler>();
    builder.Services.AddScoped<ClickUpWebhookHandler>();
    builder.Services.AddHostedService<WebhookPollingFallbackService>();
    builder.Services.AddHostedService<SmartKb.Api.EmbeddingCacheEvictionService>();
}
else
{
    builder.Services.AddSingleton<InMemoryAuditEventWriter>();
    builder.Services.AddSingleton<IAuditEventWriter>(sp => sp.GetRequiredService<InMemoryAuditEventWriter>());
    builder.Services.AddSingleton<IAuditEventQueryService>(sp =>
        new InMemoryAuditEventQueryService(sp.GetRequiredService<InMemoryAuditEventWriter>()));
}

var app = builder.Build();

var version = Assembly.GetExecutingAssembly()
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.1.0";

app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<TenantContextMiddleware>();

app.MapHealthChecks("/healthz").AllowAnonymous();

app.MapGet("/api/health", () =>
{
    var status = new HealthStatus(
        Service: "SmartKb.Api",
        Status: "Healthy",
        Version: version,
        Timestamp: DateTimeOffset.UtcNow);
    return Results.Ok(status);
}).AllowAnonymous();

app.MapGet("/", () => Results.Ok(new { service = "SmartKb.Api", status = "running" }))
    .AllowAnonymous();

app.MapGet("/api/me", (ITenantContextAccessor tenantAccessor, HttpContext ctx) =>
{
    var roles = PermissionAuthorizationHandler.GetAppRoles(ctx.User).ToList();
    var tenant = tenantAccessor.Current;
    return Results.Ok(new
    {
        userId = tenant?.UserId ?? ctx.User.FindFirst("oid")?.Value ?? ctx.User.FindFirst("sub")?.Value,
        name = ctx.User.FindFirst("name")?.Value,
        tenantId = tenant?.TenantId ?? ctx.User.FindFirst("tid")?.Value,
        correlationId = tenant?.CorrelationId,
        roles = roles.Select(r => r.ToString()),
    });
});

// --- Connector Admin Endpoints ---

app.MapGet("/api/admin/connectors", async (
    ITenantContextAccessor tenantAccessor,
    ConnectorAdminService service) =>
{
    var tenant = tenantAccessor.Current!;
    var result = await service.ListAsync(tenant.TenantId);
    return Results.Ok(ApiResponse<ConnectorListResponse>.Success(result, tenant.CorrelationId));
}).RequirePermission("connector:manage");

app.MapPost("/api/admin/connectors", async (
    CreateConnectorRequest request,
    ITenantContextAccessor tenantAccessor,
    ConnectorAdminService service) =>
{
    var tenant = tenantAccessor.Current!;
    var (response, validation) = await service.CreateAsync(
        tenant.TenantId, tenant.UserId, tenant.CorrelationId, request);

    if (validation is not null)
        return Results.UnprocessableEntity(ApiResponse<ConnectorValidationResult>.Failure(
            string.Join("; ", validation.Errors), tenant.CorrelationId));

    return Results.Created($"/api/admin/connectors/{response!.Id}",
        ApiResponse<ConnectorResponse>.Success(response, tenant.CorrelationId));
}).RequirePermission("connector:manage");

app.MapGet("/api/admin/connectors/{connectorId:guid}", async (
    Guid connectorId,
    ITenantContextAccessor tenantAccessor,
    ConnectorAdminService service) =>
{
    var tenant = tenantAccessor.Current!;
    var response = await service.GetAsync(tenant.TenantId, connectorId);
    return response is null
        ? Results.NotFound(ApiResponse<object>.Failure("Connector not found.", tenant.CorrelationId))
        : Results.Ok(ApiResponse<ConnectorResponse>.Success(response, tenant.CorrelationId));
}).RequirePermission("connector:manage");

app.MapPut("/api/admin/connectors/{connectorId:guid}", async (
    Guid connectorId,
    UpdateConnectorRequest request,
    ITenantContextAccessor tenantAccessor,
    ConnectorAdminService service) =>
{
    var tenant = tenantAccessor.Current!;
    var (response, validation, notFound) = await service.UpdateAsync(
        tenant.TenantId, tenant.UserId, tenant.CorrelationId, connectorId, request);

    if (notFound)
        return Results.NotFound(ApiResponse<object>.Failure("Connector not found.", tenant.CorrelationId));
    if (validation is not null)
        return Results.UnprocessableEntity(ApiResponse<ConnectorValidationResult>.Failure(
            string.Join("; ", validation.Errors), tenant.CorrelationId));

    return Results.Ok(ApiResponse<ConnectorResponse>.Success(response!, tenant.CorrelationId));
}).RequirePermission("connector:manage");

app.MapDelete("/api/admin/connectors/{connectorId:guid}", async (
    Guid connectorId,
    ITenantContextAccessor tenantAccessor,
    ConnectorAdminService service) =>
{
    var tenant = tenantAccessor.Current!;
    var deleted = await service.DeleteAsync(
        tenant.TenantId, tenant.UserId, tenant.CorrelationId, connectorId);
    return deleted
        ? Results.Ok(ApiResponse<object>.Success(new { deleted = true }, tenant.CorrelationId))
        : Results.NotFound(ApiResponse<object>.Failure("Connector not found.", tenant.CorrelationId));
}).RequirePermission("connector:manage");

app.MapPost("/api/admin/connectors/{connectorId:guid}/enable", async (
    Guid connectorId,
    ITenantContextAccessor tenantAccessor,
    ConnectorAdminService service) =>
{
    var tenant = tenantAccessor.Current!;
    var (found, validation, response) = await service.EnableAsync(
        tenant.TenantId, tenant.UserId, tenant.CorrelationId, connectorId);

    if (!found)
        return Results.NotFound(ApiResponse<object>.Failure("Connector not found.", tenant.CorrelationId));
    if (validation is not null)
        return Results.UnprocessableEntity(ApiResponse<ConnectorValidationResult>.Failure(
            string.Join("; ", validation.Errors), tenant.CorrelationId));

    return Results.Ok(ApiResponse<ConnectorResponse>.Success(response!, tenant.CorrelationId));
}).RequirePermission("connector:manage");

app.MapPost("/api/admin/connectors/{connectorId:guid}/disable", async (
    Guid connectorId,
    ITenantContextAccessor tenantAccessor,
    ConnectorAdminService service) =>
{
    var tenant = tenantAccessor.Current!;
    var (found, response) = await service.DisableAsync(
        tenant.TenantId, tenant.UserId, tenant.CorrelationId, connectorId);

    if (!found)
        return Results.NotFound(ApiResponse<object>.Failure("Connector not found.", tenant.CorrelationId));

    return Results.Ok(ApiResponse<ConnectorResponse>.Success(response!, tenant.CorrelationId));
}).RequirePermission("connector:manage");

app.MapPost("/api/admin/connectors/{connectorId:guid}/test", async (
    Guid connectorId,
    ITenantContextAccessor tenantAccessor,
    ConnectorAdminService service) =>
{
    var tenant = tenantAccessor.Current!;
    var result = await service.TestConnectionAsync(
        tenant.TenantId, tenant.UserId, tenant.CorrelationId, connectorId);
    return result is null
        ? Results.NotFound(ApiResponse<object>.Failure("Connector not found.", tenant.CorrelationId))
        : Results.Ok(ApiResponse<TestConnectionResponse>.Success(result, tenant.CorrelationId));
}).RequirePermission("connector:manage");

app.MapPost("/api/admin/connectors/{connectorId:guid}/sync-now", async (
    Guid connectorId,
    SyncNowRequest request,
    ITenantContextAccessor tenantAccessor,
    ConnectorAdminService service) =>
{
    var tenant = tenantAccessor.Current!;
    var (syncRunId, notFound) = await service.SyncNowAsync(
        tenant.TenantId, tenant.UserId, tenant.CorrelationId, connectorId, request);

    if (notFound)
        return Results.NotFound(ApiResponse<object>.Failure("Connector not found.", tenant.CorrelationId));

    return Results.Accepted($"/api/admin/connectors/{connectorId}/sync-runs/{syncRunId}",
        ApiResponse<object>.Success(new { syncRunId, status = "Pending" }, tenant.CorrelationId));
}).RequirePermission("connector:sync");

app.MapPost("/api/admin/connectors/{connectorId:guid}/preview", async (
    Guid connectorId,
    PreviewRequest request,
    ITenantContextAccessor tenantAccessor,
    ConnectorAdminService service) =>
{
    var tenant = tenantAccessor.Current!;
    var result = await service.PreviewAsync(
        tenant.TenantId, tenant.UserId, tenant.CorrelationId, connectorId, request);
    return result is null
        ? Results.NotFound(ApiResponse<object>.Failure("Connector not found.", tenant.CorrelationId))
        : Results.Ok(ApiResponse<PreviewResponse>.Success(result, tenant.CorrelationId));
}).RequirePermission("connector:manage");

app.MapPost("/api/admin/connectors/{connectorId:guid}/validate-mapping", (
    Guid connectorId,
    FieldMappingConfig mapping,
    ITenantContextAccessor tenantAccessor,
    ConnectorAdminService service) =>
{
    var tenant = tenantAccessor.Current!;
    var result = service.ValidateFieldMapping(mapping);
    return Results.Ok(ApiResponse<ConnectorValidationResult>.Success(result, tenant.CorrelationId));
}).RequirePermission("connector:manage");

app.MapGet("/api/admin/connectors/{connectorId:guid}/sync-runs", async (
    Guid connectorId,
    ITenantContextAccessor tenantAccessor,
    ConnectorAdminService service) =>
{
    var tenant = tenantAccessor.Current!;
    var result = await service.ListSyncRunsAsync(tenant.TenantId, connectorId);
    return result is null
        ? Results.NotFound(ApiResponse<object>.Failure("Connector not found.", tenant.CorrelationId))
        : Results.Ok(ApiResponse<SyncRunListResponse>.Success(result, tenant.CorrelationId));
}).RequirePermission("connector:manage");

app.MapGet("/api/admin/connectors/{connectorId:guid}/sync-runs/{syncRunId:guid}", async (
    Guid connectorId,
    Guid syncRunId,
    ITenantContextAccessor tenantAccessor,
    ConnectorAdminService service) =>
{
    var tenant = tenantAccessor.Current!;
    var result = await service.GetSyncRunAsync(tenant.TenantId, connectorId, syncRunId);
    return result is null
        ? Results.NotFound(ApiResponse<object>.Failure("Sync run not found.", tenant.CorrelationId))
        : Results.Ok(ApiResponse<SyncRunSummary>.Success(result, tenant.CorrelationId));
}).RequirePermission("connector:manage");

// --- OAuth Endpoints (P3-019) ---

app.MapGet("/api/admin/connectors/{connectorId:guid}/oauth/authorize", async (
    Guid connectorId,
    ITenantContextAccessor tenantAccessor,
    ConnectorAdminService service) =>
{
    var tenant = tenantAccessor.Current!;
    var result = await service.GetOAuthAuthorizeUrlAsync(tenant.TenantId, connectorId);
    if (result is null)
        return Results.NotFound(ApiResponse<object>.Failure(
            "Connector not found, not configured for OAuth, or OAuth is not enabled.", tenant.CorrelationId));

    return Results.Ok(ApiResponse<OAuthAuthorizeUrlResponse>.Success(result, tenant.CorrelationId));
}).RequirePermission("connector:manage");

app.MapGet("/api/admin/connectors/{connectorId:guid}/oauth/callback", async (
    Guid connectorId,
    string code,
    string state,
    ITenantContextAccessor tenantAccessor,
    ConnectorAdminService service) =>
{
    var tenant = tenantAccessor.Current!;
    var (response, notFound, invalidState) = await service.HandleOAuthCallbackAsync(
        tenant.TenantId, tenant.UserId, tenant.CorrelationId, connectorId, code, state);

    if (notFound)
        return Results.NotFound(ApiResponse<object>.Failure("Connector not found.", tenant.CorrelationId));
    if (invalidState)
        return Results.BadRequest(ApiResponse<object>.Failure("Invalid or expired state parameter.", tenant.CorrelationId));

    return Results.Ok(ApiResponse<OAuthCallbackResponse>.Success(response!, tenant.CorrelationId));
}).RequirePermission("connector:manage");

// --- Synonym Map Admin Endpoints (P3-004) ---

app.MapGet("/api/admin/synonym-rules", async (
    string? groupName,
    ITenantContextAccessor tenantAccessor,
    ISynonymMapService service) =>
{
    var tenant = tenantAccessor.Current!;
    var result = await service.ListAsync(tenant.TenantId, groupName);
    return Results.Ok(ApiResponse<SynonymRuleListResponse>.Success(result, tenant.CorrelationId));
}).RequirePermission("connector:manage");

app.MapGet("/api/admin/synonym-rules/{ruleId:guid}", async (
    Guid ruleId,
    ITenantContextAccessor tenantAccessor,
    ISynonymMapService service) =>
{
    var tenant = tenantAccessor.Current!;
    var result = await service.GetAsync(tenant.TenantId, ruleId);
    return result is null
        ? Results.NotFound(ApiResponse<object>.Failure("Synonym rule not found.", tenant.CorrelationId))
        : Results.Ok(ApiResponse<SynonymRuleResponse>.Success(result, tenant.CorrelationId));
}).RequirePermission("connector:manage");

app.MapPost("/api/admin/synonym-rules", async (
    CreateSynonymRuleRequest request,
    ITenantContextAccessor tenantAccessor,
    ISynonymMapService service) =>
{
    var tenant = tenantAccessor.Current!;
    var (response, validation) = await service.CreateAsync(
        tenant.TenantId, tenant.UserId, tenant.CorrelationId, request);

    if (validation is not null)
        return Results.UnprocessableEntity(ApiResponse<SynonymRuleValidationResult>.Failure(
            string.Join("; ", validation.Errors), tenant.CorrelationId));

    return Results.Created($"/api/admin/synonym-rules/{response!.Id}",
        ApiResponse<SynonymRuleResponse>.Success(response, tenant.CorrelationId));
}).RequirePermission("connector:manage");

app.MapPut("/api/admin/synonym-rules/{ruleId:guid}", async (
    Guid ruleId,
    UpdateSynonymRuleRequest request,
    ITenantContextAccessor tenantAccessor,
    ISynonymMapService service) =>
{
    var tenant = tenantAccessor.Current!;
    var (response, validation, notFound) = await service.UpdateAsync(
        tenant.TenantId, tenant.UserId, tenant.CorrelationId, ruleId, request);

    if (notFound)
        return Results.NotFound(ApiResponse<object>.Failure("Synonym rule not found.", tenant.CorrelationId));
    if (validation is not null)
        return Results.UnprocessableEntity(ApiResponse<SynonymRuleValidationResult>.Failure(
            string.Join("; ", validation.Errors), tenant.CorrelationId));

    return Results.Ok(ApiResponse<SynonymRuleResponse>.Success(response!, tenant.CorrelationId));
}).RequirePermission("connector:manage");

app.MapDelete("/api/admin/synonym-rules/{ruleId:guid}", async (
    Guid ruleId,
    ITenantContextAccessor tenantAccessor,
    ISynonymMapService service) =>
{
    var tenant = tenantAccessor.Current!;
    var deleted = await service.DeleteAsync(
        tenant.TenantId, tenant.UserId, tenant.CorrelationId, ruleId);
    return deleted
        ? Results.Ok(ApiResponse<object>.Success(new { deleted = true }, tenant.CorrelationId))
        : Results.NotFound(ApiResponse<object>.Failure("Synonym rule not found.", tenant.CorrelationId));
}).RequirePermission("connector:manage");

app.MapPost("/api/admin/synonym-rules/sync", async (
    ITenantContextAccessor tenantAccessor,
    ISynonymMapService service) =>
{
    var tenant = tenantAccessor.Current!;
    var result = await service.SyncToSearchAsync(tenant.TenantId, tenant.CorrelationId);
    return result.Success
        ? Results.Ok(ApiResponse<SynonymMapSyncResult>.Success(result, tenant.CorrelationId))
        : Results.UnprocessableEntity(ApiResponse<SynonymMapSyncResult>.Failure(
            result.ErrorDetail ?? "Sync failed.", tenant.CorrelationId));
}).RequirePermission("connector:manage");

app.MapPost("/api/admin/synonym-rules/seed", async (
    SeedSynonymRulesRequest request,
    ITenantContextAccessor tenantAccessor,
    ISynonymMapService service) =>
{
    var tenant = tenantAccessor.Current!;
    var count = await service.SeedDefaultsAsync(
        tenant.TenantId, tenant.UserId, tenant.CorrelationId, request.OverwriteExisting);
    return Results.Ok(ApiResponse<object>.Success(new { seeded = count }, tenant.CorrelationId));
}).RequirePermission("connector:manage");

// --- Webhook Receiver Endpoints (anonymous — HMAC-verified inside handler) ---

app.MapPost("/api/webhooks/ado/{connectorId:guid}", async (
    Guid connectorId,
    HttpContext httpContext,
    AdoWebhookHandler handler) =>
{
    // Read raw body for signature verification.
    using var reader = new StreamReader(httpContext.Request.Body);
    var body = await reader.ReadToEndAsync();
    var authHeader = httpContext.Request.Headers.Authorization.FirstOrDefault();

    var (statusCode, message) = await handler.HandleAsync(connectorId, body, authHeader);
    return Results.Json(new { message }, statusCode: statusCode);
}).AllowAnonymous();

// Graph change notification endpoint — supports validation handshake (POST with validationToken query param)
// and change notification processing. Anonymous because Graph validates via clientState.
app.MapPost("/api/webhooks/msgraph/{connectorId:guid}", async (
    Guid connectorId,
    HttpContext httpContext,
    SharePointWebhookHandler handler) =>
{
    // Graph subscription validation handshake: POST with ?validationToken=...
    var validationToken = httpContext.Request.Query["validationToken"].FirstOrDefault();
    if (!string.IsNullOrEmpty(validationToken))
    {
        var (code, contentType, body) = handler.HandleValidation(validationToken);
        return Results.Content(body, contentType, statusCode: code);
    }

    // Normal change notification.
    using var reader = new StreamReader(httpContext.Request.Body);
    var requestBody = await reader.ReadToEndAsync();

    var (statusCode, message) = await handler.HandleNotificationAsync(connectorId, requestBody);
    return Results.Json(new { message }, statusCode: statusCode);
}).AllowAnonymous();

// HubSpot webhook endpoint — validates HMAC-SHA256 signature via X-HubSpot-Signature-v3 header.
app.MapPost("/api/webhooks/hubspot/{connectorId:guid}", async (
    Guid connectorId,
    HttpContext httpContext,
    HubSpotWebhookHandler handler) =>
{
    using var reader = new StreamReader(httpContext.Request.Body);
    var body = await reader.ReadToEndAsync();
    var signatureHeader = httpContext.Request.Headers["X-HubSpot-Signature-v3"].FirstOrDefault()
        ?? httpContext.Request.Headers["X-HubSpot-Signature"].FirstOrDefault();
    var timestampHeader = httpContext.Request.Headers["X-HubSpot-Request-Timestamp"].FirstOrDefault();

    var (statusCode, message) = await handler.HandleAsync(connectorId, body, signatureHeader, timestampHeader);
    return Results.Json(new { message }, statusCode: statusCode);
}).AllowAnonymous();

// ClickUp webhook endpoint — validates HMAC-SHA256 signature via X-Signature header.
app.MapPost("/api/webhooks/clickup/{connectorId:guid}", async (
    Guid connectorId,
    HttpContext httpContext,
    ClickUpWebhookHandler handler) =>
{
    using var reader = new StreamReader(httpContext.Request.Body);
    var body = await reader.ReadToEndAsync();
    var signatureHeader = httpContext.Request.Headers["X-Signature"].FirstOrDefault();

    var (statusCode, message) = await handler.HandleAsync(connectorId, body, signatureHeader);
    return Results.Json(new { message }, statusCode: statusCode);
}).AllowAnonymous();

// --- Session Endpoints ---

app.MapPost("/api/sessions", async (
    CreateSessionRequest request,
    ITenantContextAccessor tenantAccessor,
    ISessionService sessionService) =>
{
    var tenant = tenantAccessor.Current!;
    var response = await sessionService.CreateSessionAsync(tenant.TenantId, tenant.UserId, request);
    return Results.Created($"/api/sessions/{response.SessionId}",
        ApiResponse<SessionResponse>.Success(response, tenant.CorrelationId));
}).RequirePermission("chat:query");

app.MapGet("/api/sessions", async (
    ITenantContextAccessor tenantAccessor,
    ISessionService sessionService) =>
{
    var tenant = tenantAccessor.Current!;
    var response = await sessionService.ListSessionsAsync(tenant.TenantId, tenant.UserId);
    return Results.Ok(ApiResponse<SessionListResponse>.Success(response, tenant.CorrelationId));
}).RequirePermission("chat:query");

app.MapGet("/api/sessions/{sessionId:guid}", async (
    Guid sessionId,
    ITenantContextAccessor tenantAccessor,
    ISessionService sessionService) =>
{
    var tenant = tenantAccessor.Current!;
    var response = await sessionService.GetSessionAsync(tenant.TenantId, tenant.UserId, sessionId);
    return response is null
        ? Results.NotFound(ApiResponse<object>.Failure("Session not found.", tenant.CorrelationId))
        : Results.Ok(ApiResponse<SessionResponse>.Success(response, tenant.CorrelationId));
}).RequirePermission("chat:query");

app.MapDelete("/api/sessions/{sessionId:guid}", async (
    Guid sessionId,
    ITenantContextAccessor tenantAccessor,
    ISessionService sessionService) =>
{
    var tenant = tenantAccessor.Current!;
    var deleted = await sessionService.DeleteSessionAsync(tenant.TenantId, tenant.UserId, sessionId);
    return deleted
        ? Results.Ok(ApiResponse<object>.Success(new { deleted = true }, tenant.CorrelationId))
        : Results.NotFound(ApiResponse<object>.Failure("Session not found.", tenant.CorrelationId));
}).RequirePermission("chat:query");

app.MapGet("/api/sessions/{sessionId:guid}/messages", async (
    Guid sessionId,
    ITenantContextAccessor tenantAccessor,
    ISessionService sessionService) =>
{
    var tenant = tenantAccessor.Current!;
    var response = await sessionService.GetMessagesAsync(tenant.TenantId, tenant.UserId, sessionId);
    return response is null
        ? Results.NotFound(ApiResponse<object>.Failure("Session not found.", tenant.CorrelationId))
        : Results.Ok(ApiResponse<MessageListResponse>.Success(response, tenant.CorrelationId));
}).RequirePermission("chat:query");

app.MapPost("/api/sessions/{sessionId:guid}/messages", async (
    Guid sessionId,
    SendMessageRequest request,
    ITenantContextAccessor tenantAccessor,
    ISessionService sessionService) =>
{
    var tenant = tenantAccessor.Current!;
    // P0-014: Inject JWT-extracted user groups for ACL enforcement.
    var effectiveRequest = request with
    {
        UserGroups = tenant.UserGroups.Count > 0 ? tenant.UserGroups : request.UserGroups,
    };
    var response = await sessionService.SendMessageAsync(
        tenant.TenantId, tenant.UserId, tenant.CorrelationId, sessionId, effectiveRequest);
    return response is null
        ? Results.NotFound(ApiResponse<object>.Failure("Session not found or expired.", tenant.CorrelationId))
        : Results.Ok(ApiResponse<SessionChatResponse>.Success(response, tenant.CorrelationId));
}).RequirePermission("chat:query");

// --- Feedback Endpoints ---

app.MapPost("/api/sessions/{sessionId:guid}/messages/{messageId:guid}/feedback", async (
    Guid sessionId,
    Guid messageId,
    SubmitFeedbackRequest request,
    ITenantContextAccessor tenantAccessor,
    IFeedbackService feedbackService) =>
{
    var tenant = tenantAccessor.Current!;
    try
    {
        var response = await feedbackService.SubmitFeedbackAsync(
            tenant.TenantId, tenant.UserId, tenant.CorrelationId,
            sessionId, messageId, request);
        return Results.Ok(ApiResponse<FeedbackResponse>.Success(response, tenant.CorrelationId));
    }
    catch (InvalidOperationException ex)
    {
        return Results.UnprocessableEntity(
            ApiResponse<object>.Failure(ex.Message, tenant.CorrelationId));
    }
}).RequirePermission("chat:feedback");

app.MapGet("/api/sessions/{sessionId:guid}/messages/{messageId:guid}/feedback", async (
    Guid sessionId,
    Guid messageId,
    ITenantContextAccessor tenantAccessor,
    IFeedbackService feedbackService) =>
{
    var tenant = tenantAccessor.Current!;
    var response = await feedbackService.GetFeedbackAsync(
        tenant.TenantId, tenant.UserId, sessionId, messageId);
    return response is null
        ? Results.NotFound(ApiResponse<object>.Failure("Feedback not found.", tenant.CorrelationId))
        : Results.Ok(ApiResponse<FeedbackResponse>.Success(response, tenant.CorrelationId));
}).RequirePermission("chat:feedback");

app.MapGet("/api/sessions/{sessionId:guid}/feedbacks", async (
    Guid sessionId,
    ITenantContextAccessor tenantAccessor,
    IFeedbackService feedbackService) =>
{
    var tenant = tenantAccessor.Current!;
    var response = await feedbackService.ListFeedbacksAsync(
        tenant.TenantId, tenant.UserId, sessionId);
    return response is null
        ? Results.NotFound(ApiResponse<object>.Failure("Session not found.", tenant.CorrelationId))
        : Results.Ok(ApiResponse<FeedbackListResponse>.Success(response, tenant.CorrelationId));
}).RequirePermission("chat:feedback");

// --- Outcome Endpoints ---

app.MapPost("/api/sessions/{sessionId:guid}/outcome", async (
    Guid sessionId,
    RecordOutcomeRequest request,
    ITenantContextAccessor tenantAccessor,
    IOutcomeService outcomeService) =>
{
    var tenant = tenantAccessor.Current!;
    try
    {
        var response = await outcomeService.RecordOutcomeAsync(
            tenant.TenantId, tenant.UserId, tenant.CorrelationId,
            sessionId, request);
        return Results.Created($"/api/sessions/{sessionId}/outcome",
            ApiResponse<OutcomeResponse>.Success(response, tenant.CorrelationId));
    }
    catch (InvalidOperationException ex)
    {
        return Results.UnprocessableEntity(
            ApiResponse<object>.Failure(ex.Message, tenant.CorrelationId));
    }
}).RequirePermission("chat:outcome");

app.MapGet("/api/sessions/{sessionId:guid}/outcome", async (
    Guid sessionId,
    ITenantContextAccessor tenantAccessor,
    IOutcomeService outcomeService) =>
{
    var tenant = tenantAccessor.Current!;
    var response = await outcomeService.GetOutcomesAsync(
        tenant.TenantId, tenant.UserId, sessionId);
    return response is null
        ? Results.NotFound(ApiResponse<object>.Failure("Session not found.", tenant.CorrelationId))
        : Results.Ok(ApiResponse<OutcomeListResponse>.Success(response, tenant.CorrelationId));
}).RequirePermission("chat:outcome");

// --- Escalation Draft Endpoints ---

app.MapPost("/api/escalations/draft", async (
    CreateEscalationDraftRequest request,
    ITenantContextAccessor tenantAccessor,
    IEscalationDraftService escalationService) =>
{
    var tenant = tenantAccessor.Current!;
    try
    {
        var response = await escalationService.CreateDraftAsync(
            tenant.TenantId, tenant.UserId, tenant.CorrelationId, request);
        return Results.Created($"/api/escalations/draft/{response.DraftId}",
            ApiResponse<EscalationDraftResponse>.Success(response, tenant.CorrelationId));
    }
    catch (InvalidOperationException ex)
    {
        return Results.UnprocessableEntity(
            ApiResponse<object>.Failure(ex.Message, tenant.CorrelationId));
    }
}).RequirePermission("chat:query");

app.MapGet("/api/escalations/draft/{draftId:guid}", async (
    Guid draftId,
    ITenantContextAccessor tenantAccessor,
    IEscalationDraftService escalationService) =>
{
    var tenant = tenantAccessor.Current!;
    var response = await escalationService.GetDraftAsync(tenant.TenantId, tenant.UserId, draftId);
    return response is null
        ? Results.NotFound(ApiResponse<object>.Failure("Escalation draft not found.", tenant.CorrelationId))
        : Results.Ok(ApiResponse<EscalationDraftResponse>.Success(response, tenant.CorrelationId));
}).RequirePermission("chat:query");

app.MapGet("/api/sessions/{sessionId:guid}/escalations/drafts", async (
    Guid sessionId,
    ITenantContextAccessor tenantAccessor,
    IEscalationDraftService escalationService) =>
{
    var tenant = tenantAccessor.Current!;
    var response = await escalationService.ListDraftsAsync(tenant.TenantId, tenant.UserId, sessionId);
    return response is null
        ? Results.NotFound(ApiResponse<object>.Failure("Session not found.", tenant.CorrelationId))
        : Results.Ok(ApiResponse<EscalationDraftListResponse>.Success(response, tenant.CorrelationId));
}).RequirePermission("chat:query");

app.MapPut("/api/escalations/draft/{draftId:guid}", async (
    Guid draftId,
    UpdateEscalationDraftRequest request,
    ITenantContextAccessor tenantAccessor,
    IEscalationDraftService escalationService) =>
{
    var tenant = tenantAccessor.Current!;
    var (response, notFound) = await escalationService.UpdateDraftAsync(
        tenant.TenantId, tenant.UserId, draftId, request);
    return notFound
        ? Results.NotFound(ApiResponse<object>.Failure("Escalation draft not found.", tenant.CorrelationId))
        : Results.Ok(ApiResponse<EscalationDraftResponse>.Success(response!, tenant.CorrelationId));
}).RequirePermission("chat:query");

app.MapGet("/api/escalations/draft/{draftId:guid}/export", async (
    Guid draftId,
    ITenantContextAccessor tenantAccessor,
    IEscalationDraftService escalationService) =>
{
    var tenant = tenantAccessor.Current!;
    var response = await escalationService.ExportDraftAsMarkdownAsync(
        tenant.TenantId, tenant.UserId, draftId);
    return response is null
        ? Results.NotFound(ApiResponse<object>.Failure("Escalation draft not found.", tenant.CorrelationId))
        : Results.Ok(ApiResponse<EscalationDraftExportResponse>.Success(response, tenant.CorrelationId));
}).RequirePermission("chat:query");

app.MapPost("/api/escalations/draft/{draftId:guid}/approve", async (
    Guid draftId,
    ApproveEscalationDraftRequest request,
    ITenantContextAccessor tenantAccessor,
    IEscalationDraftService escalationService) =>
{
    var tenant = tenantAccessor.Current!;
    try
    {
        var result = await escalationService.ApproveAndCreateExternalAsync(
            tenant.TenantId, tenant.UserId, tenant.CorrelationId, draftId, request);
        if (result is null)
            return Results.NotFound(ApiResponse<object>.Failure("Escalation draft not found.", tenant.CorrelationId));

        return result.ExternalStatus == "Created"
            ? Results.Ok(ApiResponse<ExternalEscalationResult>.Success(result, tenant.CorrelationId))
            : Results.UnprocessableEntity(ApiResponse<ExternalEscalationResult>.Success(result, tenant.CorrelationId));
    }
    catch (InvalidOperationException ex)
    {
        return Results.UnprocessableEntity(
            ApiResponse<object>.Failure(ex.Message, tenant.CorrelationId));
    }
}).RequirePermission("chat:query");

app.MapDelete("/api/escalations/draft/{draftId:guid}", async (
    Guid draftId,
    ITenantContextAccessor tenantAccessor,
    IEscalationDraftService escalationService) =>
{
    var tenant = tenantAccessor.Current!;
    var deleted = await escalationService.DeleteDraftAsync(tenant.TenantId, tenant.UserId, draftId);
    return deleted
        ? Results.Ok(ApiResponse<object>.Success(new { deleted = true }, tenant.CorrelationId))
        : Results.NotFound(ApiResponse<object>.Failure("Escalation draft not found.", tenant.CorrelationId));
}).RequirePermission("chat:query");

// --- Chat Endpoint (stateless, kept for backward compatibility) ---

app.MapPost("/api/chat", async (
    ChatRequest request,
    ITenantContextAccessor tenantAccessor,
    HttpContext httpContext) =>
{
    var tenant = tenantAccessor.Current!;
    var orchestrator = httpContext.RequestServices.GetService<IChatOrchestrator>();
    if (orchestrator is null)
        return Results.Json(
            ApiResponse<object>.Failure("Chat orchestration is not configured. Ensure OpenAI and Search Service are set up.", tenant.CorrelationId),
            statusCode: 503);

    // P0-014: Inject JWT-extracted user groups for ACL enforcement.
    // Merge with any groups provided in the request body (server-side groups take precedence).
    var effectiveRequest = request with
    {
        UserGroups = tenant.UserGroups.Count > 0 ? tenant.UserGroups : request.UserGroups,
    };

    var response = await orchestrator.OrchestrateAsync(
        tenant.TenantId, tenant.UserId, tenant.CorrelationId, effectiveRequest);
    return Results.Ok(ApiResponse<ChatResponse>.Success(response, tenant.CorrelationId));
}).RequirePermission("chat:query");

// --- Audit Endpoints ---

app.MapGet("/api/audit/events", async (
    ITenantContextAccessor tenantAccessor,
    IAuditEventQueryService auditQuery,
    string? eventType,
    string? actorId,
    string? correlationId,
    DateTimeOffset? from,
    DateTimeOffset? to,
    int? page,
    int? pageSize,
    CancellationToken ct) =>
{
    var tenant = tenantAccessor.Current!;
    var request = new AuditEventQueryRequest
    {
        EventType = eventType,
        ActorId = actorId,
        CorrelationId = correlationId,
        From = from,
        To = to,
        Page = page ?? 1,
        PageSize = pageSize ?? 50,
    };
    var result = await auditQuery.QueryAsync(tenant.TenantId, request, ct);
    return Results.Ok(ApiResponse<AuditEventListResponse>.Success(result, tenant.CorrelationId));
}).RequirePermission("audit:read");

app.MapGet("/api/audit/events/export", async (
    HttpContext httpContext,
    ITenantContextAccessor tenantAccessor,
    IAuditEventQueryService auditQuery,
    string? eventType,
    string? actorId,
    DateTimeOffset? from,
    DateTimeOffset? to,
    DateTimeOffset? afterTimestamp,
    Guid? afterId,
    int? limit,
    CancellationToken ct) =>
{
    var tenant = tenantAccessor.Current!;
    var cursor = new AuditExportCursor
    {
        EventType = eventType,
        ActorId = actorId,
        From = from,
        To = to,
        AfterTimestamp = afterTimestamp,
        AfterId = afterId,
        Limit = limit ?? 1000,
    };

    httpContext.Response.ContentType = "application/x-ndjson";
    httpContext.Response.Headers["X-Correlation-Id"] = tenant.CorrelationId;

    AuditEventResponse? lastEvent = null;
    var count = 0;

    await foreach (var auditEvent in auditQuery.ExportAsync(tenant.TenantId, cursor, ct))
    {
        var json = System.Text.Json.JsonSerializer.Serialize(auditEvent);
        await httpContext.Response.WriteAsync(json + "\n", ct);
        lastEvent = auditEvent;
        count++;
    }

    // Write cursor metadata as last NDJSON line if there are results
    if (lastEvent is not null)
    {
        var nextCursor = new
        {
            __cursor = true,
            afterTimestamp = lastEvent.Timestamp,
            afterId = lastEvent.EventId,
            hasMore = count >= cursor.Limit,
        };
        var cursorJson = System.Text.Json.JsonSerializer.Serialize(nextCursor);
        await httpContext.Response.WriteAsync(cursorJson + "\n", ct);
    }
}).RequirePermission("audit:export");

// --- Retrieval Tuning Endpoints (P1-007) ---

app.MapGet("/api/admin/retrieval-settings", async (
    ITenantContextAccessor tenantAccessor,
    ITenantRetrievalSettingsService settingsService) =>
{
    var tenant = tenantAccessor.Current!;
    var result = await settingsService.GetSettingsAsync(tenant.TenantId);
    return Results.Ok(ApiResponse<RetrievalSettingsResponse>.Success(result, tenant.CorrelationId));
}).RequirePermission("connector:manage");

app.MapPut("/api/admin/retrieval-settings", async (
    UpdateRetrievalSettingsRequest request,
    ITenantContextAccessor tenantAccessor,
    ITenantRetrievalSettingsService settingsService) =>
{
    var tenant = tenantAccessor.Current!;
    var result = await settingsService.UpdateSettingsAsync(tenant.TenantId, request);
    return Results.Ok(ApiResponse<RetrievalSettingsResponse>.Success(result, tenant.CorrelationId));
}).RequirePermission("connector:manage");

app.MapDelete("/api/admin/retrieval-settings", async (
    ITenantContextAccessor tenantAccessor,
    ITenantRetrievalSettingsService settingsService) =>
{
    var tenant = tenantAccessor.Current!;
    var deleted = await settingsService.ResetSettingsAsync(tenant.TenantId);
    return deleted
        ? Results.Ok(ApiResponse<object>.Success(new { reset = true }, tenant.CorrelationId))
        : Results.NotFound(ApiResponse<object>.Failure("No tenant overrides found.", tenant.CorrelationId));
}).RequirePermission("connector:manage");

// --- SLO Status Endpoint (P0-022) ---

app.MapGet("/api/admin/slo/status", (
    ITenantContextAccessor tenantAccessor,
    SloSettings sloSettingsInstance) =>
{
    var tenant = tenantAccessor.Current!;
    return Results.Ok(ApiResponse<object>.Success(new
    {
        targets = new
        {
            answerLatencyP95TargetMs = sloSettingsInstance.AnswerLatencyP95TargetMs,
            availabilityTargetPercent = sloSettingsInstance.AvailabilityTargetPercent,
            syncLagP95TargetMinutes = sloSettingsInstance.SyncLagP95TargetMinutes,
            noEvidenceRateThreshold = sloSettingsInstance.NoEvidenceRateThreshold,
            deadLetterDepthThreshold = sloSettingsInstance.DeadLetterDepthThreshold,
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
}).RequirePermission("connector:manage");

// --- Pattern Distillation Endpoints (P1-005) ---

app.MapGet("/api/admin/patterns/candidates", async (
    ITenantContextAccessor tenantAccessor,
    IPatternDistillationService distillationService) =>
{
    var tenant = tenantAccessor.Current!;
    var result = await distillationService.FindCandidatesAsync(tenant.TenantId);
    return Results.Ok(ApiResponse<DistillationCandidateListResponse>.Success(result, tenant.CorrelationId));
}).RequirePermission("connector:manage");

app.MapPost("/api/admin/patterns/distill", async (
    ITenantContextAccessor tenantAccessor,
    IPatternDistillationService distillationService) =>
{
    var tenant = tenantAccessor.Current!;
    var result = await distillationService.DistillAsync(
        tenant.TenantId, tenant.UserId, tenant.CorrelationId);
    return Results.Ok(ApiResponse<DistillationResult>.Success(result, tenant.CorrelationId));
}).RequirePermission("connector:manage");

// --- Pattern Governance Endpoints (P1-006) ---

app.MapGet("/api/patterns/governance-queue", async (
    ITenantContextAccessor tenantAccessor,
    IPatternGovernanceService governanceService,
    string? trustLevel,
    string? productArea,
    int? page,
    int? pageSize) =>
{
    var tenant = tenantAccessor.Current!;
    var result = await governanceService.GetGovernanceQueueAsync(
        tenant.TenantId, trustLevel, productArea, page ?? 1, pageSize ?? 20);
    return Results.Ok(ApiResponse<PatternGovernanceQueueResponse>.Success(result, tenant.CorrelationId));
}).RequirePermission("pattern:approve");

app.MapGet("/api/patterns/{patternId}", async (
    string patternId,
    ITenantContextAccessor tenantAccessor,
    IPatternGovernanceService governanceService) =>
{
    var tenant = tenantAccessor.Current!;
    var result = await governanceService.GetPatternDetailAsync(tenant.TenantId, patternId);
    return result is null
        ? Results.NotFound()
        : Results.Ok(ApiResponse<PatternDetail>.Success(result, tenant.CorrelationId));
}).RequirePermission("pattern:approve");

app.MapPost("/api/patterns/{patternId}/review", async (
    string patternId,
    ReviewPatternRequest request,
    ITenantContextAccessor tenantAccessor,
    IPatternGovernanceService governanceService) =>
{
    var tenant = tenantAccessor.Current!;
    var result = await governanceService.ReviewPatternAsync(
        tenant.TenantId, patternId, tenant.UserId, tenant.CorrelationId, request);
    return result is null
        ? Results.NotFound()
        : Results.Ok(ApiResponse<PatternGovernanceResult>.Success(result, tenant.CorrelationId));
}).RequirePermission("pattern:approve");

app.MapPost("/api/patterns/{patternId}/approve", async (
    string patternId,
    ApprovePatternRequest request,
    ITenantContextAccessor tenantAccessor,
    IPatternGovernanceService governanceService) =>
{
    var tenant = tenantAccessor.Current!;
    var result = await governanceService.ApprovePatternAsync(
        tenant.TenantId, patternId, tenant.UserId, tenant.CorrelationId, request);
    return result is null
        ? Results.NotFound()
        : Results.Ok(ApiResponse<PatternGovernanceResult>.Success(result, tenant.CorrelationId));
}).RequirePermission("pattern:approve");

app.MapPost("/api/patterns/{patternId}/deprecate", async (
    string patternId,
    DeprecatePatternRequest request,
    ITenantContextAccessor tenantAccessor,
    IPatternGovernanceService governanceService) =>
{
    var tenant = tenantAccessor.Current!;
    var result = await governanceService.DeprecatePatternAsync(
        tenant.TenantId, patternId, tenant.UserId, tenant.CorrelationId, request);
    return result is null
        ? Results.NotFound()
        : Results.Ok(ApiResponse<PatternGovernanceResult>.Success(result, tenant.CorrelationId));
}).RequirePermission("pattern:deprecate");

// --- Pattern Maintenance Endpoints (P2-004) ---

app.MapPost("/api/admin/patterns/detect-contradictions", async (
    ITenantContextAccessor tenantAccessor,
    IContradictionDetectionService contradictionService) =>
{
    var tenant = tenantAccessor.Current!;
    var result = await contradictionService.DetectContradictionsAsync(
        tenant.TenantId, tenant.UserId, tenant.CorrelationId);
    return Results.Ok(ApiResponse<ContradictionDetectionResult>.Success(result, tenant.CorrelationId));
}).RequirePermission("pattern:approve");

app.MapGet("/api/admin/patterns/contradictions", async (
    ITenantContextAccessor tenantAccessor,
    IContradictionDetectionService contradictionService,
    string? status,
    int? page,
    int? pageSize) =>
{
    var tenant = tenantAccessor.Current!;
    var result = await contradictionService.GetContradictionsAsync(
        tenant.TenantId, status, page ?? 1, pageSize ?? 20);
    return Results.Ok(ApiResponse<ContradictionListResponse>.Success(result, tenant.CorrelationId));
}).RequirePermission("pattern:approve");

app.MapPost("/api/admin/patterns/contradictions/{id}/resolve", async (
    Guid id,
    ResolveContradictionRequest request,
    ITenantContextAccessor tenantAccessor,
    IContradictionDetectionService contradictionService) =>
{
    var tenant = tenantAccessor.Current!;
    var result = await contradictionService.ResolveContradictionAsync(
        id, tenant.TenantId, tenant.UserId, tenant.CorrelationId, request);
    return result is null
        ? Results.NotFound()
        : Results.Ok(ApiResponse<ContradictionSummary>.Success(result, tenant.CorrelationId));
}).RequirePermission("pattern:approve");

app.MapPost("/api/admin/patterns/detect-maintenance", async (
    ITenantContextAccessor tenantAccessor,
    IPatternMaintenanceService maintenanceService) =>
{
    var tenant = tenantAccessor.Current!;
    var result = await maintenanceService.DetectMaintenanceIssuesAsync(
        tenant.TenantId, tenant.UserId, tenant.CorrelationId);
    return Results.Ok(ApiResponse<MaintenanceDetectionResult>.Success(result, tenant.CorrelationId));
}).RequirePermission("pattern:approve");

app.MapGet("/api/admin/patterns/maintenance-tasks", async (
    ITenantContextAccessor tenantAccessor,
    IPatternMaintenanceService maintenanceService,
    string? status,
    string? taskType,
    int? page,
    int? pageSize) =>
{
    var tenant = tenantAccessor.Current!;
    var result = await maintenanceService.GetMaintenanceTasksAsync(
        tenant.TenantId, status, taskType, page ?? 1, pageSize ?? 20);
    return Results.Ok(ApiResponse<MaintenanceTaskListResponse>.Success(result, tenant.CorrelationId));
}).RequirePermission("pattern:approve");

app.MapPost("/api/admin/patterns/maintenance-tasks/{id}/resolve", async (
    Guid id,
    ResolveMaintenanceTaskRequest request,
    ITenantContextAccessor tenantAccessor,
    IPatternMaintenanceService maintenanceService) =>
{
    var tenant = tenantAccessor.Current!;
    var result = await maintenanceService.ResolveTaskAsync(
        id, tenant.TenantId, tenant.UserId, tenant.CorrelationId, request);
    return result is null
        ? Results.NotFound()
        : Results.Ok(ApiResponse<MaintenanceTaskSummary>.Success(result, tenant.CorrelationId));
}).RequirePermission("pattern:approve");

app.MapPost("/api/admin/patterns/maintenance-tasks/{id}/dismiss", async (
    Guid id,
    ResolveMaintenanceTaskRequest request,
    ITenantContextAccessor tenantAccessor,
    IPatternMaintenanceService maintenanceService) =>
{
    var tenant = tenantAccessor.Current!;
    var result = await maintenanceService.DismissTaskAsync(
        id, tenant.TenantId, tenant.UserId, tenant.CorrelationId, request);
    return result is null
        ? Results.NotFound()
        : Results.Ok(ApiResponse<MaintenanceTaskSummary>.Success(result, tenant.CorrelationId));
}).RequirePermission("pattern:approve");

// --- Secrets Status Endpoint ---

app.MapGet("/api/admin/secrets/status", (
    ITenantContextAccessor tenantAccessor,
    OpenAiKeyProvider openAiKeyProvider,
    IServiceProvider sp) =>
{
    var tenant = tenantAccessor.Current!;
    var keyVaultConfigured = sp.GetService<ISecretProvider>() is not null;

    bool openAiConfigured;
    try
    {
        var key = openAiKeyProvider.GetApiKey();
        openAiConfigured = !string.IsNullOrWhiteSpace(key);
    }
    catch (InvalidOperationException)
    {
        openAiConfigured = false;
    }

    return Results.Ok(new
    {
        tenantId = tenant.TenantId,
        keyVaultConfigured,
        openAiKeyConfigured = openAiConfigured,
        openAiModel = openAiKeyProvider.GetModel(),
    });
}).RequirePermission("connector:manage");

// --- Dead-Letter Queue Inspection ---

app.MapGet("/api/admin/ingestion/dead-letters", async (
    ITenantContextAccessor tenantAccessor,
    HttpContext httpContext) =>
{
    var tenant = tenantAccessor.Current!;
    var dlService = httpContext.RequestServices.GetService<DeadLetterService>();
    if (dlService is null)
        return Results.Ok(ApiResponse<object>.Success(
            new { messages = Array.Empty<object>(), count = 0, serviceBusConfigured = false },
            tenant.CorrelationId));

    var maxParam = httpContext.Request.Query["maxMessages"].FirstOrDefault();
    var maxMessages = int.TryParse(maxParam, out var m) ? m : 20;
    var result = await dlService.PeekAsync(maxMessages);
    return Results.Ok(ApiResponse<DeadLetterListResponse>.Success(result, tenant.CorrelationId));
}).RequirePermission("connector:manage");

// --- Webhook Status Endpoint (P1-008) ---

app.MapGet("/api/admin/connectors/{connectorId}/webhooks", async (
    Guid connectorId,
    ITenantContextAccessor tenantAccessor,
    IWebhookStatusService webhookStatusService) =>
{
    var tenant = tenantAccessor.Current!;
    var result = await webhookStatusService.GetByConnectorAsync(tenant.TenantId, connectorId);
    return Results.Ok(ApiResponse<WebhookStatusListResponse>.Success(result, tenant.CorrelationId));
}).RequirePermission("connector:manage");

app.MapGet("/api/admin/webhooks", async (
    ITenantContextAccessor tenantAccessor,
    IWebhookStatusService webhookStatusService) =>
{
    var tenant = tenantAccessor.Current!;
    var result = await webhookStatusService.GetAllAsync(tenant.TenantId);
    return Results.Ok(ApiResponse<WebhookStatusListResponse>.Success(result, tenant.CorrelationId));
}).RequirePermission("connector:manage");

// --- Diagnostics Summary Endpoint (P1-008) ---

app.MapGet("/api/admin/diagnostics/summary", async (
    ITenantContextAccessor tenantAccessor,
    IWebhookStatusService webhookStatusService,
    OpenAiKeyProvider openAiKeyProvider,
    IServiceProvider sp) =>
{
    var tenant = tenantAccessor.Current!;
    var summary = await webhookStatusService.GetDiagnosticsSummaryAsync(tenant.TenantId);

    var keyVaultConfigured = sp.GetService<ISecretProvider>() is not null;
    bool openAiConfigured;
    try { openAiConfigured = !string.IsNullOrWhiteSpace(openAiKeyProvider.GetApiKey()); }
    catch (InvalidOperationException) { openAiConfigured = false; }
    var searchConfigured = sp.GetService<SearchIndexClient>() is not null;
    var sbConfigured = sp.GetService<ServiceBusClient>() is not null;

    var enriched = summary with
    {
        ServiceBusConfigured = sbConfigured,
        KeyVaultConfigured = keyVaultConfigured,
        OpenAiConfigured = openAiConfigured,
        SearchServiceConfigured = searchConfigured,
    };

    return Results.Ok(ApiResponse<DiagnosticsSummaryResponse>.Success(enriched, tenant.CorrelationId));
}).RequirePermission("connector:manage");

// --- Routing Rules CRUD (P1-009) ---

app.MapGet("/api/admin/routing-rules", async (
    ITenantContextAccessor tenantAccessor,
    IRoutingRuleService ruleService) =>
{
    var tenant = tenantAccessor.Current!;
    var result = await ruleService.GetRulesAsync(tenant.TenantId);
    return Results.Ok(ApiResponse<RoutingRuleListResponse>.Success(result, tenant.CorrelationId));
}).RequirePermission("connector:manage");

app.MapGet("/api/admin/routing-rules/{ruleId:guid}", async (
    Guid ruleId,
    ITenantContextAccessor tenantAccessor,
    IRoutingRuleService ruleService) =>
{
    var tenant = tenantAccessor.Current!;
    var result = await ruleService.GetRuleAsync(tenant.TenantId, ruleId);
    return result is null
        ? Results.NotFound(ApiResponse<RoutingRuleDto>.Failure("Routing rule not found.", tenant.CorrelationId))
        : Results.Ok(ApiResponse<RoutingRuleDto>.Success(result, tenant.CorrelationId));
}).RequirePermission("connector:manage");

app.MapPost("/api/admin/routing-rules", async (
    CreateRoutingRuleRequest request,
    ITenantContextAccessor tenantAccessor,
    IRoutingRuleService ruleService) =>
{
    var tenant = tenantAccessor.Current!;
    var result = await ruleService.CreateRuleAsync(
        tenant.TenantId, tenant.UserId, tenant.CorrelationId, request);
    return Results.Created($"/api/admin/routing-rules/{result.RuleId}",
        ApiResponse<RoutingRuleDto>.Success(result, tenant.CorrelationId));
}).RequirePermission("connector:manage");

app.MapPut("/api/admin/routing-rules/{ruleId:guid}", async (
    Guid ruleId,
    UpdateRoutingRuleRequest request,
    ITenantContextAccessor tenantAccessor,
    IRoutingRuleService ruleService) =>
{
    var tenant = tenantAccessor.Current!;
    var result = await ruleService.UpdateRuleAsync(
        tenant.TenantId, tenant.UserId, tenant.CorrelationId, ruleId, request);
    return result is null
        ? Results.NotFound(ApiResponse<RoutingRuleDto>.Failure("Routing rule not found.", tenant.CorrelationId))
        : Results.Ok(ApiResponse<RoutingRuleDto>.Success(result, tenant.CorrelationId));
}).RequirePermission("connector:manage");

app.MapDelete("/api/admin/routing-rules/{ruleId:guid}", async (
    Guid ruleId,
    ITenantContextAccessor tenantAccessor,
    IRoutingRuleService ruleService) =>
{
    var tenant = tenantAccessor.Current!;
    var deleted = await ruleService.DeleteRuleAsync(
        tenant.TenantId, tenant.UserId, tenant.CorrelationId, ruleId);
    return deleted
        ? Results.Ok(ApiResponse<object>.Success(new { deleted = true }, tenant.CorrelationId))
        : Results.NotFound(ApiResponse<object>.Failure("Routing rule not found.", tenant.CorrelationId));
}).RequirePermission("connector:manage");

// --- Routing Analytics + Improvement (P1-009) ---

app.MapGet("/api/admin/routing/analytics", async (
    int? windowDays,
    ITenantContextAccessor tenantAccessor,
    IRoutingAnalyticsService analyticsService) =>
{
    var tenant = tenantAccessor.Current!;
    var result = await analyticsService.GetAnalyticsAsync(tenant.TenantId, windowDays);
    return Results.Ok(ApiResponse<RoutingAnalyticsSummary>.Success(result, tenant.CorrelationId));
}).RequirePermission("connector:manage");

app.MapPost("/api/admin/routing/recommendations/generate", async (
    ITenantContextAccessor tenantAccessor,
    IRoutingImprovementService improvementService) =>
{
    var tenant = tenantAccessor.Current!;
    var result = await improvementService.GenerateRecommendationsAsync(
        tenant.TenantId, tenant.UserId, tenant.CorrelationId);
    return Results.Ok(ApiResponse<RoutingRecommendationListResponse>.Success(result, tenant.CorrelationId));
}).RequirePermission("connector:manage");

app.MapGet("/api/admin/routing/recommendations", async (
    string? status,
    ITenantContextAccessor tenantAccessor,
    IRoutingImprovementService improvementService) =>
{
    var tenant = tenantAccessor.Current!;
    var result = await improvementService.GetRecommendationsAsync(tenant.TenantId, status);
    return Results.Ok(ApiResponse<RoutingRecommendationListResponse>.Success(result, tenant.CorrelationId));
}).RequirePermission("connector:manage");

app.MapPost("/api/admin/routing/recommendations/{recommendationId:guid}/apply", async (
    Guid recommendationId,
    ApplyRecommendationRequest? request,
    ITenantContextAccessor tenantAccessor,
    IRoutingImprovementService improvementService) =>
{
    var tenant = tenantAccessor.Current!;
    var result = await improvementService.ApplyRecommendationAsync(
        tenant.TenantId, tenant.UserId, tenant.CorrelationId,
        recommendationId, request);
    return result is null
        ? Results.NotFound(ApiResponse<RoutingRecommendationDto>.Failure("Recommendation not found or not pending.", tenant.CorrelationId))
        : Results.Ok(ApiResponse<RoutingRecommendationDto>.Success(result, tenant.CorrelationId));
}).RequirePermission("connector:manage");

app.MapPost("/api/admin/routing/recommendations/{recommendationId:guid}/dismiss", async (
    Guid recommendationId,
    ITenantContextAccessor tenantAccessor,
    IRoutingImprovementService improvementService) =>
{
    var tenant = tenantAccessor.Current!;
    var dismissed = await improvementService.DismissRecommendationAsync(
        tenant.TenantId, tenant.UserId, tenant.CorrelationId, recommendationId);
    return dismissed
        ? Results.Ok(ApiResponse<object>.Success(new { dismissed = true }, tenant.CorrelationId))
        : Results.NotFound(ApiResponse<object>.Failure("Recommendation not found or not pending.", tenant.CorrelationId));
}).RequirePermission("connector:manage");

// ──── Team Playbooks (P2-002) ────

app.MapGet("/api/admin/playbooks", async (
    ITenantContextAccessor tenantAccessor,
    ITeamPlaybookService playbookService) =>
{
    var tenant = tenantAccessor.Current!;
    var result = await playbookService.GetPlaybooksAsync(tenant.TenantId);
    return Results.Ok(ApiResponse<TeamPlaybookListResponse>.Success(result, tenant.CorrelationId));
}).RequirePermission("connector:manage");

app.MapGet("/api/admin/playbooks/{playbookId:guid}", async (
    Guid playbookId,
    ITenantContextAccessor tenantAccessor,
    ITeamPlaybookService playbookService) =>
{
    var tenant = tenantAccessor.Current!;
    var result = await playbookService.GetPlaybookAsync(tenant.TenantId, playbookId);
    return result is null
        ? Results.NotFound(ApiResponse<TeamPlaybookDto>.Failure("Playbook not found.", tenant.CorrelationId))
        : Results.Ok(ApiResponse<TeamPlaybookDto>.Success(result, tenant.CorrelationId));
}).RequirePermission("connector:manage");

app.MapGet("/api/admin/playbooks/team/{teamName}", async (
    string teamName,
    ITenantContextAccessor tenantAccessor,
    ITeamPlaybookService playbookService) =>
{
    var tenant = tenantAccessor.Current!;
    var result = await playbookService.GetPlaybookByTeamAsync(tenant.TenantId, teamName);
    return result is null
        ? Results.NotFound(ApiResponse<TeamPlaybookDto>.Failure("Playbook not found for team.", tenant.CorrelationId))
        : Results.Ok(ApiResponse<TeamPlaybookDto>.Success(result, tenant.CorrelationId));
}).RequirePermission("connector:manage");

app.MapPost("/api/admin/playbooks", async (
    CreateTeamPlaybookRequest request,
    ITenantContextAccessor tenantAccessor,
    ITeamPlaybookService playbookService) =>
{
    var tenant = tenantAccessor.Current!;
    var result = await playbookService.CreatePlaybookAsync(
        tenant.TenantId, tenant.UserId, tenant.CorrelationId, request);
    return Results.Created($"/api/admin/playbooks/{result.Id}",
        ApiResponse<TeamPlaybookDto>.Success(result, tenant.CorrelationId));
}).RequirePermission("connector:manage");

app.MapPut("/api/admin/playbooks/{playbookId:guid}", async (
    Guid playbookId,
    UpdateTeamPlaybookRequest request,
    ITenantContextAccessor tenantAccessor,
    ITeamPlaybookService playbookService) =>
{
    var tenant = tenantAccessor.Current!;
    var result = await playbookService.UpdatePlaybookAsync(
        tenant.TenantId, tenant.UserId, tenant.CorrelationId, playbookId, request);
    return result is null
        ? Results.NotFound(ApiResponse<TeamPlaybookDto>.Failure("Playbook not found.", tenant.CorrelationId))
        : Results.Ok(ApiResponse<TeamPlaybookDto>.Success(result, tenant.CorrelationId));
}).RequirePermission("connector:manage");

app.MapDelete("/api/admin/playbooks/{playbookId:guid}", async (
    Guid playbookId,
    ITenantContextAccessor tenantAccessor,
    ITeamPlaybookService playbookService) =>
{
    var tenant = tenantAccessor.Current!;
    var deleted = await playbookService.DeletePlaybookAsync(
        tenant.TenantId, tenant.UserId, tenant.CorrelationId, playbookId);
    return deleted
        ? Results.Ok(ApiResponse<object>.Success(new { deleted = true }, tenant.CorrelationId))
        : Results.NotFound(ApiResponse<object>.Failure("Playbook not found.", tenant.CorrelationId));
}).RequirePermission("connector:manage");

app.MapPost("/api/admin/playbooks/validate", async (
    PlaybookValidateRequest request,
    ITenantContextAccessor tenantAccessor,
    ITeamPlaybookService playbookService) =>
{
    var tenant = tenantAccessor.Current!;
    var result = await playbookService.ValidateDraftAsync(
        tenant.TenantId, request.TargetTeam, request.Draft);
    return Results.Ok(ApiResponse<PlaybookValidationResult>.Success(result, tenant.CorrelationId));
}).RequirePermission("chat:query");

// ──── Privacy & Compliance Endpoints (P2-001) ────

app.MapGet("/api/admin/privacy/pii-policy", async (
    ITenantContextAccessor tenantAccessor,
    IPiiPolicyService piiPolicyService) =>
{
    var tenant = tenantAccessor.Current!;
    var policy = await piiPolicyService.GetPolicyAsync(tenant.TenantId);
    return policy is not null
        ? Results.Ok(ApiResponse<PiiPolicyResponse>.Success(policy, tenant.CorrelationId))
        : Results.Ok(ApiResponse<object>.Success(new { message = "No custom PII policy. Using defaults (all types, redact mode)." }, tenant.CorrelationId));
}).RequirePermission("privacy:manage");

app.MapPut("/api/admin/privacy/pii-policy", async (
    PiiPolicyUpdateRequest request,
    ITenantContextAccessor tenantAccessor,
    IPiiPolicyService piiPolicyService) =>
{
    var tenant = tenantAccessor.Current!;
    try
    {
        var result = await piiPolicyService.UpsertPolicyAsync(tenant.TenantId, request, tenant.UserId);
        return Results.Ok(ApiResponse<PiiPolicyResponse>.Success(result, tenant.CorrelationId));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(ApiResponse<object>.Failure(ex.Message, tenant.CorrelationId));
    }
}).RequirePermission("privacy:manage");

app.MapDelete("/api/admin/privacy/pii-policy", async (
    ITenantContextAccessor tenantAccessor,
    IPiiPolicyService piiPolicyService) =>
{
    var tenant = tenantAccessor.Current!;
    var deleted = await piiPolicyService.DeletePolicyAsync(tenant.TenantId, tenant.UserId);
    return deleted
        ? Results.Ok(ApiResponse<object>.Success(new { reset = true }, tenant.CorrelationId))
        : Results.NotFound(ApiResponse<object>.Failure("No custom PII policy found.", tenant.CorrelationId));
}).RequirePermission("privacy:manage");

app.MapGet("/api/admin/privacy/retention", async (
    ITenantContextAccessor tenantAccessor,
    IRetentionCleanupService retentionService) =>
{
    var tenant = tenantAccessor.Current!;
    var result = await retentionService.GetPoliciesAsync(tenant.TenantId);
    return Results.Ok(ApiResponse<RetentionPolicyResponse>.Success(result, tenant.CorrelationId));
}).RequirePermission("privacy:manage");

app.MapPut("/api/admin/privacy/retention", async (
    RetentionPolicyUpdateRequest request,
    ITenantContextAccessor tenantAccessor,
    IRetentionCleanupService retentionService) =>
{
    var tenant = tenantAccessor.Current!;
    try
    {
        var result = await retentionService.UpsertPolicyAsync(tenant.TenantId, request, tenant.UserId);
        return Results.Ok(ApiResponse<RetentionPolicyEntry>.Success(result, tenant.CorrelationId));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(ApiResponse<object>.Failure(ex.Message, tenant.CorrelationId));
    }
}).RequirePermission("privacy:manage");

app.MapDelete("/api/admin/privacy/retention/{entityType}", async (
    string entityType,
    ITenantContextAccessor tenantAccessor,
    IRetentionCleanupService retentionService) =>
{
    var tenant = tenantAccessor.Current!;
    var deleted = await retentionService.DeletePolicyAsync(tenant.TenantId, entityType, tenant.UserId);
    return deleted
        ? Results.Ok(ApiResponse<object>.Success(new { deleted = true, entityType }, tenant.CorrelationId))
        : Results.NotFound(ApiResponse<object>.Failure($"No retention policy found for {entityType}.", tenant.CorrelationId));
}).RequirePermission("privacy:manage");

app.MapPost("/api/admin/privacy/retention/cleanup", async (
    ITenantContextAccessor tenantAccessor,
    IRetentionCleanupService retentionService) =>
{
    var tenant = tenantAccessor.Current!;
    var results = await retentionService.ExecuteCleanupAsync(tenant.TenantId);
    return Results.Ok(ApiResponse<IReadOnlyList<RetentionCleanupResult>>.Success(results, tenant.CorrelationId));
}).RequirePermission("privacy:manage");

app.MapPost("/api/admin/privacy/data-subject-deletion", async (
    DataSubjectDeletionRequest request,
    ITenantContextAccessor tenantAccessor,
    IDataSubjectDeletionService deletionService,
    ILogger<Program> logger) =>
{
    var tenant = tenantAccessor.Current!;
    try
    {
        var result = await deletionService.RequestDeletionAsync(tenant.TenantId, request.SubjectId, tenant.UserId);
        return Results.Ok(ApiResponse<DataSubjectDeletionResponse>.Success(result, tenant.CorrelationId));
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Data subject deletion request failed. TenantId={TenantId}", tenant.TenantId);
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
}).RequirePermission("privacy:manage");

app.MapGet("/api/admin/privacy/data-subject-deletion", async (
    ITenantContextAccessor tenantAccessor,
    IDataSubjectDeletionService deletionService) =>
{
    var tenant = tenantAccessor.Current!;
    var result = await deletionService.ListDeletionRequestsAsync(tenant.TenantId);
    return Results.Ok(ApiResponse<DataSubjectDeletionListResponse>.Success(result, tenant.CorrelationId));
}).RequirePermission("privacy:manage");

app.MapGet("/api/admin/privacy/data-subject-deletion/{requestId:guid}", async (
    Guid requestId,
    ITenantContextAccessor tenantAccessor,
    IDataSubjectDeletionService deletionService) =>
{
    var tenant = tenantAccessor.Current!;
    var result = await deletionService.GetDeletionRequestAsync(tenant.TenantId, requestId);
    return result is not null
        ? Results.Ok(ApiResponse<DataSubjectDeletionResponse>.Success(result, tenant.CorrelationId))
        : Results.NotFound(ApiResponse<object>.Failure("Deletion request not found.", tenant.CorrelationId));
}).RequirePermission("privacy:manage");

// ──── Retention Measurable Execution Endpoints (P2-005) ────

app.MapGet("/api/admin/privacy/retention/history", async (
    string? entityType,
    int? skip,
    int? take,
    ITenantContextAccessor tenantAccessor,
    IRetentionCleanupService retentionService) =>
{
    var tenant = tenantAccessor.Current!;
    var result = await retentionService.GetExecutionHistoryAsync(
        tenant.TenantId, entityType, skip ?? 0, take ?? 50);
    return Results.Ok(ApiResponse<RetentionExecutionHistoryResponse>.Success(result, tenant.CorrelationId));
}).RequirePermission("privacy:manage");

app.MapGet("/api/admin/privacy/retention/compliance", async (
    ITenantContextAccessor tenantAccessor,
    IRetentionCleanupService retentionService) =>
{
    var tenant = tenantAccessor.Current!;
    var report = await retentionService.GetComplianceReportAsync(tenant.TenantId);
    return Results.Ok(ApiResponse<RetentionComplianceReport>.Success(report, tenant.CorrelationId));
}).RequirePermission("privacy:manage");

// --- Cost Optimization Endpoints (P2-003) ---

app.MapGet("/api/admin/cost-settings", async (
    ITenantContextAccessor tenantAccessor,
    ITenantCostSettingsService costSettingsService) =>
{
    var tenant = tenantAccessor.Current!;
    var result = await costSettingsService.GetSettingsAsync(tenant.TenantId);
    return Results.Ok(ApiResponse<CostSettingsResponse>.Success(result, tenant.CorrelationId));
}).RequirePermission("connector:manage");

app.MapPut("/api/admin/cost-settings", async (
    UpdateCostSettingsRequest request,
    ITenantContextAccessor tenantAccessor,
    ITenantCostSettingsService costSettingsService) =>
{
    var tenant = tenantAccessor.Current!;
    var result = await costSettingsService.UpdateSettingsAsync(tenant.TenantId, request);
    return Results.Ok(ApiResponse<CostSettingsResponse>.Success(result, tenant.CorrelationId));
}).RequirePermission("connector:manage");

app.MapDelete("/api/admin/cost-settings", async (
    ITenantContextAccessor tenantAccessor,
    ITenantCostSettingsService costSettingsService) =>
{
    var tenant = tenantAccessor.Current!;
    var deleted = await costSettingsService.ResetSettingsAsync(tenant.TenantId);
    return deleted
        ? Results.Ok(ApiResponse<object>.Success(new { reset = true }, tenant.CorrelationId))
        : Results.NotFound(ApiResponse<object>.Failure("No tenant cost overrides found.", tenant.CorrelationId));
}).RequirePermission("connector:manage");

app.MapGet("/api/admin/token-usage/summary", async (
    ITenantContextAccessor tenantAccessor,
    ITokenUsageService tokenUsageService,
    int? days) =>
{
    var tenant = tenantAccessor.Current!;
    var periodDays = days ?? 30;
    var periodEnd = DateTimeOffset.UtcNow;
    var periodStart = periodEnd.AddDays(-periodDays);
    var result = await tokenUsageService.GetSummaryAsync(tenant.TenantId, periodStart, periodEnd);
    return Results.Ok(ApiResponse<TokenUsageSummary>.Success(result, tenant.CorrelationId));
}).RequirePermission("connector:manage");

app.MapGet("/api/admin/token-usage/daily", async (
    ITenantContextAccessor tenantAccessor,
    ITokenUsageService tokenUsageService,
    int? days) =>
{
    var tenant = tenantAccessor.Current!;
    var periodDays = days ?? 30;
    var periodEnd = DateTimeOffset.UtcNow;
    var periodStart = periodEnd.AddDays(-periodDays);
    var result = await tokenUsageService.GetDailyBreakdownAsync(tenant.TenantId, periodStart, periodEnd);
    return Results.Ok(ApiResponse<IReadOnlyList<DailyUsageBreakdown>>.Success(result, tenant.CorrelationId));
}).RequirePermission("connector:manage");

app.MapGet("/api/admin/token-usage/budget-check", async (
    ITenantContextAccessor tenantAccessor,
    ITokenUsageService tokenUsageService) =>
{
    var tenant = tenantAccessor.Current!;
    var result = await tokenUsageService.CheckBudgetAsync(tenant.TenantId);
    return Results.Ok(ApiResponse<BudgetCheckResult>.Success(result, tenant.CorrelationId));
}).RequirePermission("connector:manage");

// --- Index Migration Admin Endpoints (P3-005) ---
// Service resolved from DI at request time; returns 503 when SearchService is not configured.

app.MapGet("/api/admin/index-migrations/{indexType}/current", async (
    string indexType,
    ITenantContextAccessor tenantAccessor,
    HttpContext httpContext) =>
{
    var tenant = tenantAccessor.Current!;
    var service = httpContext.RequestServices.GetService<IIndexMigrationService>();
    if (service is null)
        return Results.Json(ApiResponse<object>.Failure("Search service is not configured.", tenant.CorrelationId), statusCode: 503);
    var result = await service.GetCurrentVersionAsync(indexType);
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
    var service = httpContext.RequestServices.GetService<IIndexMigrationService>();
    if (service is null)
        return Results.Json(ApiResponse<object>.Failure("Search service is not configured.", tenant.CorrelationId), statusCode: 503);
    var result = await service.ListVersionsAsync(indexType);
    return Results.Ok(ApiResponse<IReadOnlyList<IndexSchemaVersionInfo>>.Success(result, tenant.CorrelationId));
}).RequirePermission("connector:manage");

app.MapGet("/api/admin/index-migrations/{indexType}/plan", async (
    string indexType,
    ITenantContextAccessor tenantAccessor,
    HttpContext httpContext) =>
{
    var tenant = tenantAccessor.Current!;
    var service = httpContext.RequestServices.GetService<IIndexMigrationService>();
    if (service is null)
        return Results.Json(ApiResponse<object>.Failure("Search service is not configured.", tenant.CorrelationId), statusCode: 503);
    var result = await service.PlanMigrationAsync(indexType);
    return Results.Ok(ApiResponse<MigrationPlan>.Success(result, tenant.CorrelationId));
}).RequirePermission("connector:manage");

app.MapPost("/api/admin/index-migrations/{indexType}/execute", async (
    string indexType,
    ITenantContextAccessor tenantAccessor,
    HttpContext httpContext) =>
{
    var tenant = tenantAccessor.Current!;
    var service = httpContext.RequestServices.GetService<IIndexMigrationService>();
    if (service is null)
        return Results.Json(ApiResponse<object>.Failure("Search service is not configured.", tenant.CorrelationId), statusCode: 503);
    var result = await service.ExecuteMigrationAsync(indexType, tenant.UserId);
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
    var service = httpContext.RequestServices.GetService<IIndexMigrationService>();
    if (service is null)
        return Results.Json(ApiResponse<object>.Failure("Search service is not configured.", tenant.CorrelationId), statusCode: 503);
    var result = await service.RollbackAsync(indexType, tenant.UserId);
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
    var service = httpContext.RequestServices.GetService<IIndexMigrationService>();
    if (service is null)
        return Results.Json(ApiResponse<object>.Failure("Search service is not configured.", tenant.CorrelationId), statusCode: 503);
    var result = await service.EnsureVersionTrackingAsync(indexType, tenant.UserId);
    return Results.Ok(ApiResponse<IndexSchemaVersionInfo>.Success(result, tenant.CorrelationId));
}).RequirePermission("connector:manage");

app.MapDelete("/api/admin/index-migrations/retired/{versionId:guid}", async (
    Guid versionId,
    ITenantContextAccessor tenantAccessor,
    HttpContext httpContext) =>
{
    var tenant = tenantAccessor.Current!;
    var service = httpContext.RequestServices.GetService<IIndexMigrationService>();
    if (service is null)
        return Results.Json(ApiResponse<object>.Failure("Search service is not configured.", tenant.CorrelationId), statusCode: 503);
    var deleted = await service.DeleteRetiredVersionAsync(versionId, tenant.UserId);
    return deleted
        ? Results.Ok(ApiResponse<object>.Success(new { deleted = true }, tenant.CorrelationId))
        : Results.NotFound(ApiResponse<object>.Failure("Retired version not found or not eligible for deletion.", tenant.CorrelationId));
}).RequirePermission("connector:manage");

// Eval report endpoints (P3-021).
app.MapGet("/api/admin/eval/reports", async (
    ITenantContextAccessor tenantAccessor,
    HttpContext httpContext,
    string? runType,
    int? page,
    int? pageSize) =>
{
    var tenant = tenantAccessor.Current!;
    var evalReportService = httpContext.RequestServices.GetRequiredService<IEvalReportService>();
    var result = await evalReportService.ListReportsAsync(
        tenant.TenantId, runType, page ?? 1, pageSize ?? 20);
    return Results.Ok(ApiResponse<EvalReportListResponse>.Success(result, tenant.CorrelationId));
}).RequirePermission("connector:manage");

app.MapGet("/api/admin/eval/reports/{reportId:guid}", async (
    Guid reportId,
    ITenantContextAccessor tenantAccessor,
    HttpContext httpContext) =>
{
    var tenant = tenantAccessor.Current!;
    var evalReportService = httpContext.RequestServices.GetRequiredService<IEvalReportService>();
    var report = await evalReportService.GetReportAsync(tenant.TenantId, reportId);
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
    var evalReportService = httpContext.RequestServices.GetRequiredService<IEvalReportService>();
    var report = await evalReportService.PersistReportAsync(tenant.TenantId, request, tenant.UserId);
    return Results.Created($"/api/admin/eval/reports/{report.Id}",
        ApiResponse<EvalReportDetail>.Success(report, tenant.CorrelationId));
}).RequirePermission("connector:manage");

app.Run();

public partial class Program { }
