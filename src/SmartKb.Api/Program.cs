using System.Reflection;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
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

// Connector clients — register all IConnectorClient implementations.
builder.Services.AddHttpClient("AzureDevOps");
builder.Services.AddHttpClient("SharePoint");
builder.Services.AddSingleton<IConnectorClient, SmartKb.Contracts.Connectors.AzureDevOpsConnectorClient>();
builder.Services.AddSingleton<IConnectorClient, SmartKb.Contracts.Connectors.SharePointConnectorClient>();

// Webhook managers — register all IWebhookManager implementations.
builder.Services.AddSingleton<IWebhookManager, SmartKb.Contracts.Connectors.AdoWebhookManager>();
builder.Services.AddSingleton<IWebhookManager, SmartKb.Contracts.Connectors.SharePointWebhookManager>();

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
