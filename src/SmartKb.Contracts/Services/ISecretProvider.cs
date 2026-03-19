namespace SmartKb.Contracts.Services;

public sealed record SecretProperties(
    string Name,
    DateTimeOffset? CreatedOn,
    DateTimeOffset? UpdatedOn,
    DateTimeOffset? ExpiresOn,
    bool Enabled);

public interface ISecretProvider
{
    Task<string> GetSecretAsync(string secretName, CancellationToken cancellationToken = default);
    Task SetSecretAsync(string secretName, string secretValue, CancellationToken cancellationToken = default);
    Task DeleteSecretAsync(string secretName, CancellationToken cancellationToken = default);
    Task<SecretProperties?> GetSecretPropertiesAsync(string secretName, CancellationToken cancellationToken = default);
}
