using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Logging;

namespace SmartKb.Contracts.Services;

public sealed class KeyVaultSecretProvider : ISecretProvider
{
    private readonly SecretClient _client;
    private readonly ILogger<KeyVaultSecretProvider> _logger;

    public KeyVaultSecretProvider(SecretClient client, ILogger<KeyVaultSecretProvider> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<string> GetSecretAsync(string secretName, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Retrieving secret '{SecretName}' from Key Vault", secretName);
        var response = await _client.GetSecretAsync(secretName, cancellationToken: cancellationToken);
        _logger.LogInformation("Successfully retrieved secret '{SecretName}'", secretName);
        return response.Value.Value;
    }

    public async Task SetSecretAsync(string secretName, string secretValue, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Setting secret '{SecretName}' in Key Vault", secretName);
        await _client.SetSecretAsync(secretName, secretValue, cancellationToken);
        _logger.LogInformation("Successfully set secret '{SecretName}'", secretName);
    }

    public async Task DeleteSecretAsync(string secretName, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting secret '{SecretName}' from Key Vault", secretName);
        await _client.StartDeleteSecretAsync(secretName, cancellationToken);
        _logger.LogInformation("Successfully initiated deletion of secret '{SecretName}'", secretName);
    }

    public async Task<SecretProperties?> GetSecretPropertiesAsync(string secretName, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.GetSecretAsync(secretName, cancellationToken: cancellationToken);
            var props = response.Value.Properties;
            return new SecretProperties(
                secretName,
                props.CreatedOn,
                props.UpdatedOn,
                props.ExpiresOn,
                props.Enabled ?? true);
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning("Secret '{SecretName}' not found in Key Vault", secretName);
            return null;
        }
    }
}
