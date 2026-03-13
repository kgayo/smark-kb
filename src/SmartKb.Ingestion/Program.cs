using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Services;
using SmartKb.Ingestion;
using SmartKb.Data;
using SmartKb.Ingestion.Secrets;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHostedService<IngestionWorker>();
builder.Services.AddHealthChecks();

builder.Services.Configure<KeyVaultSettings>(builder.Configuration.GetSection(KeyVaultSettings.SectionName));

var connectionString = builder.Configuration.GetConnectionString("SmartKbDb");
if (!string.IsNullOrEmpty(connectionString))
{
    builder.Services.AddSmartKbData(connectionString);
}

var vaultUri = builder.Configuration[$"{KeyVaultSettings.SectionName}:VaultUri"];
if (!string.IsNullOrEmpty(vaultUri) && Uri.TryCreate(vaultUri, UriKind.Absolute, out var uri))
{
    builder.Services.AddSingleton(new SecretClient(uri, new DefaultAzureCredential()));
    builder.Services.AddSingleton<ISecretProvider, KeyVaultSecretProvider>();
}

var host = builder.Build();
host.Run();
