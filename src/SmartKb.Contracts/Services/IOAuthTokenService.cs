using SmartKb.Contracts.Enums;

namespace SmartKb.Contracts.Services;

/// <summary>
/// Manages OAuth authorization code flow: URL generation, code exchange, and token refresh.
/// </summary>
public interface IOAuthTokenService
{
    /// <summary>
    /// Builds the authorization URL to redirect the admin to the provider's consent page.
    /// </summary>
    string BuildAuthorizeUrl(ConnectorType connectorType, Guid connectorId, string tenantId, string? sourceConfig);

    /// <summary>
    /// Validates the state parameter from the OAuth callback.
    /// </summary>
    bool ValidateState(string state, Guid connectorId, string tenantId);

    /// <summary>
    /// Exchanges the authorization code for tokens and stores them in Key Vault.
    /// </summary>
    Task<bool> ExchangeCodeAsync(
        Guid connectorId, string tenantId, string code,
        string kvSecretName, string? sourceConfig, ConnectorType connectorType,
        CancellationToken ct = default);

    /// <summary>
    /// Resolves a valid access token. Refreshes the token if expired and updates Key Vault.
    /// Returns null if resolution fails.
    /// </summary>
    Task<string?> ResolveAccessTokenAsync(
        string kvSecretName, string? sourceConfig, ConnectorType connectorType,
        CancellationToken ct = default);
}
