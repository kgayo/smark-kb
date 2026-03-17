using Azure;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Monitor.OpenTelemetry.Exporter;
using Azure.Search.Documents.Indexes;
using Azure.Security.KeyVault.Secrets;
using Azure.Storage.Blobs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SmartKb.Contracts;
using SmartKb.Contracts.Connectors;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Services;
using SmartKb.Ingestion;
using SmartKb.Ingestion.Processing;
using SmartKb.Data;
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHostedService<IngestionWorker>();
builder.Services.AddHealthChecks();

// --- OpenTelemetry ---
var otelServiceName = "SmartKb.Ingestion";
var appInsightsConnStr = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(otelServiceName))
    .WithTracing(t =>
    {
        t.AddSource(Diagnostics.IngestionSourceName)
         .AddHttpClientInstrumentation()
         .AddSqlClientInstrumentation();

        if (!string.IsNullOrEmpty(appInsightsConnStr))
        {
            t.AddAzureMonitorTraceExporter(o => o.ConnectionString = appInsightsConnStr);
        }
    })
    .WithMetrics(m =>
    {
        m.AddHttpClientInstrumentation()
         .AddMeter(Diagnostics.MeterName);

        if (!string.IsNullOrEmpty(appInsightsConnStr))
        {
            m.AddAzureMonitorMetricExporter(o => o.ConnectionString = appInsightsConnStr);
        }
    });

builder.Logging.AddOpenTelemetry(o =>
{
    o.IncludeScopes = true;
    o.IncludeFormattedMessage = true;

    if (!string.IsNullOrEmpty(appInsightsConnStr))
    {
        o.AddAzureMonitorLogExporter(opts => opts.ConnectionString = appInsightsConnStr);
    }
});

builder.Services.Configure<KeyVaultSettings>(builder.Configuration.GetSection(KeyVaultSettings.SectionName));

// Service Bus configuration — prefer Managed Identity via FullyQualifiedNamespace; fall back to connection string.
var serviceBusSettings = new ServiceBusSettings();
builder.Configuration.GetSection(ServiceBusSettings.SectionName).Bind(serviceBusSettings);
builder.Services.AddSingleton(serviceBusSettings);

if (serviceBusSettings.IsConfigured)
{
    var sbClient = serviceBusSettings.UsesManagedIdentity
        ? new ServiceBusClient(serviceBusSettings.FullyQualifiedNamespace, new DefaultAzureCredential())
        : new ServiceBusClient(serviceBusSettings.ConnectionString);

    builder.Services.AddSingleton(sbClient);
}

// Database.
var connectionString = builder.Configuration.GetConnectionString("SmartKbDb");
if (!string.IsNullOrEmpty(connectionString))
{
    builder.Services.AddSmartKbData(connectionString);
}

// Connector clients — register all IConnectorClient implementations.
builder.Services.AddHttpClient("AzureDevOps");
builder.Services.AddHttpClient("SharePoint");
builder.Services.AddHttpClient("HubSpot");
builder.Services.AddHttpClient("ClickUp");
builder.Services.AddSingleton<IConnectorClient, SmartKb.Contracts.Connectors.AzureDevOpsConnectorClient>();
builder.Services.AddSingleton<IConnectorClient, SmartKb.Contracts.Connectors.SharePointConnectorClient>();
builder.Services.AddSingleton<IConnectorClient, SmartKb.Contracts.Connectors.HubSpotConnectorClient>();
builder.Services.AddSingleton<IConnectorClient, SmartKb.Contracts.Connectors.ClickUpConnectorClient>();

// Normalization pipeline (chunking + enrichment).
var chunkingSettings = new ChunkingSettings();
builder.Configuration.GetSection(ChunkingSettings.SectionName).Bind(chunkingSettings);
builder.Services.AddSingleton(chunkingSettings);
builder.Services.AddSingleton<IChunkingService, TextChunkingService>();
builder.Services.AddSingleton<IEnrichmentService, BaselineEnrichmentService>();
builder.Services.AddSingleton<INormalizationPipeline, NormalizationPipeline>();

// Sync job processor (scoped — uses DbContext).
builder.Services.AddScoped<SyncJobProcessor>();

// Key Vault.
var vaultUri = builder.Configuration[$"{KeyVaultSettings.SectionName}:VaultUri"];
if (!string.IsNullOrEmpty(vaultUri) && Uri.TryCreate(vaultUri, UriKind.Absolute, out var uri))
{
    builder.Services.AddSingleton(new SecretClient(uri, new DefaultAzureCredential()));
    builder.Services.AddSingleton<ISecretProvider, KeyVaultSecretProvider>();
}

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
    builder.Services.AddSingleton<IPatternIndexingService, AzureSearchPatternIndexingService>();

    // P1-004: Use FusedRetrievalService (Evidence + Pattern) when fusion is enabled; fall back to evidence-only.
    if (retrievalSettings.EnablePatternFusion)
        builder.Services.AddSingleton<IRetrievalService, FusedRetrievalService>();
    else
        builder.Services.AddSingleton<IRetrievalService, AzureSearchRetrievalService>();
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

var host = builder.Build();
host.Run();
