using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Services;

namespace SmartKb.Api.Secrets;

public static class SecretServiceExtensions
{
    public static IServiceCollection AddSecretArchitecture(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<OpenAiSettings>(configuration.GetSection(OpenAiSettings.SectionName));
        services.Configure<KeyVaultSettings>(configuration.GetSection(KeyVaultSettings.SectionName));

        services.AddSingleton<OpenAiKeyProvider>();

        var vaultUri = configuration[$"{KeyVaultSettings.SectionName}:VaultUri"];
        if (!string.IsNullOrEmpty(vaultUri) && Uri.TryCreate(vaultUri, UriKind.Absolute, out var uri))
        {
            services.AddSingleton(new SecretClient(uri, new DefaultAzureCredential()));
            services.AddSingleton<ISecretProvider, KeyVaultSecretProvider>();
        }

        return services;
    }
}
