using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Security.KeyVault.Secrets;
using SmartKb.Contracts.Connectors;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Services;
using SmartKb.Ingestion;
using SmartKb.Ingestion.Processing;
using SmartKb.Data;
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHostedService<IngestionWorker>();
builder.Services.AddHealthChecks();

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
builder.Services.AddSingleton<IConnectorClient, SmartKb.Contracts.Connectors.AzureDevOpsConnectorClient>();

// Sync job processor (scoped — uses DbContext).
builder.Services.AddScoped<SyncJobProcessor>();

// Key Vault.
var vaultUri = builder.Configuration[$"{KeyVaultSettings.SectionName}:VaultUri"];
if (!string.IsNullOrEmpty(vaultUri) && Uri.TryCreate(vaultUri, UriKind.Absolute, out var uri))
{
    builder.Services.AddSingleton(new SecretClient(uri, new DefaultAzureCredential()));
    builder.Services.AddSingleton<ISecretProvider, KeyVaultSecretProvider>();
}

var host = builder.Build();
host.Run();
