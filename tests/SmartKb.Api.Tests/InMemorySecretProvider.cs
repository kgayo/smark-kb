using SmartKb.Contracts.Services;

namespace SmartKb.Api.Tests;

/// <summary>
/// In-memory ISecretProvider for integration tests. Stores secrets in a dictionary.
/// </summary>
public sealed class InMemorySecretProvider : ISecretProvider
{
    public Dictionary<string, string> Secrets { get; } = [];
    public Dictionary<string, SecretProperties> SecretPropertiesStore { get; } = [];

    public Task<string> GetSecretAsync(string secretName, CancellationToken cancellationToken = default)
    {
        if (Secrets.TryGetValue(secretName, out var val))
            return Task.FromResult(val);
        throw new KeyNotFoundException($"Secret '{secretName}' not found.");
    }

    public Task SetSecretAsync(string secretName, string secretValue, CancellationToken cancellationToken = default)
    {
        Secrets[secretName] = secretValue;
        return Task.CompletedTask;
    }

    public Task DeleteSecretAsync(string secretName, CancellationToken cancellationToken = default)
    {
        Secrets.Remove(secretName);
        return Task.CompletedTask;
    }

    public Task<SecretProperties?> GetSecretPropertiesAsync(string secretName, CancellationToken cancellationToken = default)
    {
        if (SecretPropertiesStore.TryGetValue(secretName, out var props))
            return Task.FromResult<SecretProperties?>(props);

        if (Secrets.ContainsKey(secretName))
            return Task.FromResult<SecretProperties?>(new SecretProperties(secretName, DateTimeOffset.UtcNow.AddDays(-10), DateTimeOffset.UtcNow.AddDays(-10), null, true));

        return Task.FromResult<SecretProperties?>(null);
    }
}
