using System.Reflection;
using Azure;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Search.Documents.Indexes;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;
using SmartKb.Api.Audit;
using SmartKb.Api.Auth;
using SmartKb.Api.Connectors;
using SmartKb.Api.Secrets;
using SmartKb.Api.Tenant;
using SmartKb.Api.Webhooks;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();

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

// Normalization pipeline (chunking + enrichment).
var chunkingSettings = new SmartKb.Contracts.Configuration.ChunkingSettings();
builder.Configuration.GetSection(SmartKb.Contracts.Configuration.ChunkingSettings.SectionName).Bind(chunkingSettings);
builder.Services.AddSingleton(chunkingSettings);
builder.Services.AddSingleton<IChunkingService, SmartKb.Contracts.Services.TextChunkingService>();
builder.Services.AddSingleton<IEnrichmentService, SmartKb.Contracts.Services.BaselineEnrichmentService>();
builder.Services.AddSingleton<INormalizationPipeline, SmartKb.Contracts.Services.NormalizationPipeline>();

// Connector clients — register all IConnectorClient implementations.
builder.Services.AddHttpClient("AzureDevOps");
builder.Services.AddHttpClient("SharePoint");
builder.Services.AddSingleton<IConnectorClient, SmartKb.Contracts.Connectors.AzureDevOpsConnectorClient>();
builder.Services.AddSingleton<IConnectorClient, SmartKb.Contracts.Connectors.SharePointConnectorClient>();

// Webhook managers — register all IWebhookManager implementations.
builder.Services.AddSingleton<IWebhookManager, SmartKb.Contracts.Connectors.AdoWebhookManager>();
builder.Services.AddSingleton<IWebhookManager, SmartKb.Contracts.Connectors.SharePointWebhookManager>();

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
    builder.Services.AddSingleton<IIndexingService, AzureSearchIndexingService>();
    builder.Services.AddSingleton<IRetrievalService, AzureSearchRetrievalService>();
}

// Session settings.
var sessionSettings = new SessionSettings();
builder.Configuration.GetSection(SessionSettings.SectionName).Bind(sessionSettings);
builder.Services.AddSingleton(sessionSettings);

// Escalation settings (D-004).
var escalationSettings = new EscalationSettings();
builder.Configuration.GetSection(EscalationSettings.SectionName).Bind(escalationSettings);
builder.Services.AddSingleton(escalationSettings);

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

builder.Services.AddHttpClient("OpenAi");

builder.Services.AddSingleton<IPiiRedactionService, PiiRedactionService>();

if (!string.IsNullOrEmpty(openAiSettings.ApiKey) && searchSettings.IsConfigured)
{
    builder.Services.AddSingleton<IEmbeddingService, OpenAiEmbeddingService>();
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
    builder.Services.AddScoped<AdoWebhookHandler>();
    builder.Services.AddScoped<SharePointWebhookHandler>();
    builder.Services.AddHostedService<WebhookPollingFallbackService>();
}
else
{
    builder.Services.AddSingleton<InMemoryAuditEventWriter>();
    builder.Services.AddSingleton<IAuditEventWriter>(sp => sp.GetRequiredService<InMemoryAuditEventWriter>());
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

// --- Audit Endpoint ---

app.MapGet("/api/audit/events", (ITenantContextAccessor tenantAccessor) =>
{
    var tenant = tenantAccessor.Current!;
    return Results.Ok(new { tenantId = tenant.TenantId, message = "Audit events placeholder" });
}).RequirePermission("audit:read");

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

app.Run();

public partial class Program { }
