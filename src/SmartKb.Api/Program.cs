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
using SmartKb.Api.Endpoints;
using SmartKb.Api.Secrets;
using SmartKb.Api.Tenant;
using SmartKb.Api.Webhooks;
using SmartKb.Contracts;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using Microsoft.EntityFrameworkCore;
using SmartKb.Data;
using SmartKb.Data.Entities;
using SmartKb.Data.Repositories;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();

// --- OpenTelemetry ---
var otelServiceName = Diagnostics.ApiSourceName;
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

var azureAdClientId = builder.Configuration["AzureAd:ClientId"];
var isEntraIdConfigured = !string.IsNullOrEmpty(azureAdClientId)
    && !azureAdClientId!.StartsWith('<');

if (isEntraIdConfigured)
{
    builder.Services.AddMicrosoftIdentityWebApiAuthentication(builder.Configuration);
}
else if (builder.Environment.IsProduction())
{
    throw new InvalidOperationException(
        "Entra ID authentication must be configured in Production. " +
        "Set AzureAd:ClientId and AzureAd:TenantId to valid values.");
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

builder.Services.AddHttpClient(HttpClientNames.OAuth);

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
builder.Services.AddSingleton<IRoutingTagResolver, SmartKb.Contracts.Services.RoutingTagResolver>();

// Text extraction service for binary documents (PDF, DOCX, PPTX, XLSX).
builder.Services.AddSingleton<ITextExtractionService, SmartKb.Contracts.Services.TextExtractionService>();

// Connector clients — register all IConnectorClient implementations.
builder.Services.AddHttpClient(HttpClientNames.AzureDevOps);
builder.Services.AddHttpClient(HttpClientNames.SharePoint);
builder.Services.AddHttpClient(HttpClientNames.HubSpot);
builder.Services.AddHttpClient(HttpClientNames.ClickUp);
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
    builder.Services.AddHttpClient(HttpClientNames.EvalNotification);
    builder.Services.AddSingleton<IEvalNotificationService, WebhookEvalNotificationClient>();
}

// Secret rotation settings (P3-009).
var secretRotationSettings = new SecretRotationSettings();
builder.Configuration.GetSection(SecretRotationSettings.SectionName).Bind(secretRotationSettings);
builder.Services.AddSingleton(secretRotationSettings);

builder.Services.AddHttpClient(HttpClientNames.OpenAi);

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
    builder.Services.AddScoped<ISecretRotationService, SecretRotationService>();
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
        Service: Diagnostics.ApiSourceName,
        Status: "Healthy",
        Version: version,
        Timestamp: DateTimeOffset.UtcNow);
    return Results.Ok(status);
}).AllowAnonymous();

app.MapGet("/", () => Results.Ok(new { service = Diagnostics.ApiSourceName, status = "running" }))
    .AllowAnonymous();

app.MapGet("/api/me", (ITenantContextAccessor tenantAccessor, HttpContext ctx) =>
{
    var roles = PermissionAuthorizationHandler.GetAppRoles(ctx.User).ToList();
    var tenant = tenantAccessor.Current;
    return Results.Ok(new
    {
        userId = tenant?.UserId ?? ctx.User.FindFirst(EntraClaimTypes.ObjectId)?.Value ?? ctx.User.FindFirst(EntraClaimTypes.Subject)?.Value,
        name = ctx.User.FindFirst("name")?.Value,
        tenantId = tenant?.TenantId ?? ctx.User.FindFirst(EntraClaimTypes.TenantId)?.Value,
        correlationId = tenant?.CorrelationId,
        roles = roles.Select(r => r.ToString()),
    });
});

// --- Endpoint Groups (extracted from Program.cs for maintainability) ---
app.MapConnectorAdminEndpoints();
app.MapSearchTokenEndpoints();
app.MapWebhookEndpoints();
app.MapChatEndpoints();
app.MapAuditEndpoints();
app.MapPatternEndpoints();
app.MapDiagnosticsEndpoints();
app.MapRoutingEndpoints();
app.MapPlaybookEndpoints();
app.MapPrivacyEndpoints();
app.MapCostEndpoints();
app.MapIndexMigrationEndpoints();
app.MapEvalEndpoints();

app.Run();

public partial class Program { }
