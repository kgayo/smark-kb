using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Security.KeyVault.Secrets;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Services;
using SmartKb.Ingestion;
using SmartKb.Ingestion.Processing;
using SmartKb.Data;
using SmartKb.Ingestion.Secrets;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHostedService<IngestionWorker>();
builder.Services.AddHealthChecks();

builder.Services.Configure<KeyVaultSettings>(builder.Configuration.GetSection(KeyVaultSettings.SectionName));

// Service Bus configuration.
var serviceBusSettings = new ServiceBusSettings();
builder.Configuration.GetSection(ServiceBusSettings.SectionName).Bind(serviceBusSettings);
builder.Services.AddSingleton(serviceBusSettings);

var sbConnectionString = serviceBusSettings.ConnectionString;
if (!string.IsNullOrEmpty(sbConnectionString))
{
    builder.Services.AddSingleton(new ServiceBusClient(sbConnectionString));
}

// Database.
var connectionString = builder.Configuration.GetConnectionString("SmartKbDb");
if (!string.IsNullOrEmpty(connectionString))
{
    builder.Services.AddSmartKbData(connectionString);
}

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
