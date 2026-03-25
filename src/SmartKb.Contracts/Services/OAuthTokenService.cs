using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

/// <summary>
/// Manages OAuth authorization code flow for connector credential acquisition.
/// Stores tokens in Key Vault as a JSON-serialized <see cref="OAuthCredentials"/> blob.
/// </summary>
public sealed class OAuthTokenService : IOAuthTokenService
{
    private static readonly TimeSpan StateMaxAge = TimeSpan.FromMinutes(10);

    private readonly ISecretProvider _secretProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly OAuthSettings _settings;
    private readonly ILogger<OAuthTokenService> _logger;
    private readonly TimeProvider _timeProvider;

    public OAuthTokenService(
        ISecretProvider secretProvider,
        IHttpClientFactory httpClientFactory,
        OAuthSettings settings,
        ILogger<OAuthTokenService> logger,
        TimeProvider? timeProvider = null)
    {
        _secretProvider = secretProvider;
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public string BuildAuthorizeUrl(ConnectorType connectorType, Guid connectorId, string tenantId, string? sourceConfig)
    {
        var (authorizeUrl, clientId, scopes) = GetProviderConfig(connectorType, sourceConfig);
        var redirectUri = BuildRedirectUri(connectorId);
        var state = GenerateState(connectorId, tenantId);

        var queryParams = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["state"] = state,
        };

        if (!string.IsNullOrEmpty(scopes))
            queryParams["scope"] = scopes;

        var query = string.Join("&", queryParams.Select(kv =>
            $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

        return $"{authorizeUrl}?{query}";
    }

    public bool ValidateState(string state, Guid connectorId, string tenantId)
    {
        try
        {
            var parts = state.Split('.');
            if (parts.Length != 2) return false;

            var payload = parts[0];
            var signature = parts[1];

            // Verify HMAC signature.
            var expectedSig = ComputeHmac(payload);
            if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(signature),
                Encoding.UTF8.GetBytes(expectedSig)))
                return false;

            // Decode and validate payload.
            var payloadBytes = Convert.FromBase64String(payload);
            var payloadStr = Encoding.UTF8.GetString(payloadBytes);
            var payloadParts = payloadStr.Split('|');
            if (payloadParts.Length != 3) return false;

            var stateConnectorId = payloadParts[0];
            var stateTenantId = payloadParts[1];
            var timestampStr = payloadParts[2];

            if (stateConnectorId != connectorId.ToString() || stateTenantId != tenantId)
                return false;

            if (!long.TryParse(timestampStr, out var ticks))
                return false;

            var stateTime = new DateTimeOffset(ticks, TimeSpan.Zero);
            var now = _timeProvider.GetUtcNow();
            return (now - stateTime) <= StateMaxAge;
        }
        catch (FormatException ex)
        {
            _logger.LogWarning(ex, "OAuth state validation failed due to malformed format");
            return false;
        }
    }

    public async Task<bool> ExchangeCodeAsync(
        Guid connectorId, string tenantId, string code,
        string kvSecretName, string? sourceConfig, ConnectorType connectorType,
        CancellationToken ct = default)
    {
        var (_, clientId, scopes) = GetProviderConfig(connectorType, sourceConfig);
        var tokenUrl = GetTokenUrl(connectorType, sourceConfig);
        var redirectUri = BuildRedirectUri(connectorId);

        // Read existing credentials from KV to get client_secret.
        OAuthCredentials existingCreds;
        try
        {
            var existingJson = await _secretProvider.GetSecretAsync(kvSecretName, ct);
            existingCreds = JsonSerializer.Deserialize<OAuthCredentials>(existingJson, SharedJsonOptions.CamelCaseWrite)
                ?? new OAuthCredentials();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // No existing credentials — admin must have set up the KV secret with client_id and client_secret first.
            _logger.LogError(ex, "Failed to read OAuth client credentials from Key Vault secret {SecretName}. " +
                "Ensure the secret contains a JSON object with client_id and client_secret.", kvSecretName);
            return false;
        }

        var clientSecret = existingCreds.ClientSecret;
        if (string.IsNullOrEmpty(clientSecret))
        {
            _logger.LogError("OAuth client_secret missing from Key Vault secret {SecretName}.", kvSecretName);
            return false;
        }

        var formData = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = clientId.Length > 0 ? clientId : existingCreds.ClientId,
            ["client_secret"] = clientSecret,
        };

        var tokenResponse = await PostTokenRequestAsync(tokenUrl, formData, ct);
        if (tokenResponse is null || string.IsNullOrEmpty(tokenResponse.AccessToken))
        {
            _logger.LogError("OAuth token exchange failed for connector {ConnectorId}.", connectorId);
            return false;
        }

        var now = _timeProvider.GetUtcNow();
        var updatedCreds = existingCreds with
        {
            AccessToken = tokenResponse.AccessToken,
            RefreshToken = tokenResponse.RefreshToken ?? existingCreds.RefreshToken,
            ExpiresAt = now.AddSeconds(tokenResponse.ExpiresIn > 0 ? tokenResponse.ExpiresIn : 3600),
        };

        var credJson = JsonSerializer.Serialize(updatedCreds, SharedJsonOptions.CamelCaseWrite);
        await _secretProvider.SetSecretAsync(kvSecretName, credJson, ct);

        _logger.LogInformation("OAuth tokens stored for connector {ConnectorId} (expires at {ExpiresAt}).",
            connectorId, updatedCreds.ExpiresAt);

        return true;
    }

    public async Task<string?> ResolveAccessTokenAsync(
        string kvSecretName, string? sourceConfig, ConnectorType connectorType,
        CancellationToken ct = default)
    {
        OAuthCredentials creds;
        try
        {
            var json = await _secretProvider.GetSecretAsync(kvSecretName, ct);
            creds = JsonSerializer.Deserialize<OAuthCredentials>(json, SharedJsonOptions.CamelCaseWrite)!;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to read OAuth credentials from Key Vault secret {SecretName}.", kvSecretName);
            return null;
        }

        if (string.IsNullOrEmpty(creds.AccessToken))
        {
            _logger.LogWarning("No access token in OAuth credentials for secret {SecretName}. " +
                "OAuth authorization code flow may not have been completed.", kvSecretName);
            return null;
        }

        // Check if token is still valid (with 5-minute buffer).
        var now = _timeProvider.GetUtcNow();
        if (creds.ExpiresAt.HasValue && creds.ExpiresAt.Value > now.AddMinutes(5))
        {
            return creds.AccessToken;
        }

        // Token expired or expiring soon — refresh.
        if (string.IsNullOrEmpty(creds.RefreshToken))
        {
            _logger.LogWarning("OAuth access token expired and no refresh token available for secret {SecretName}.", kvSecretName);
            return creds.AccessToken; // Return expired token; connector client will get 401 and can surface it.
        }

        _logger.LogInformation("Refreshing expired OAuth token for secret {SecretName}.", kvSecretName);

        var tokenUrl = GetTokenUrl(connectorType, sourceConfig);
        var formData = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = creds.RefreshToken,
            ["client_id"] = creds.ClientId,
            ["client_secret"] = creds.ClientSecret,
        };

        var tokenResponse = await PostTokenRequestAsync(tokenUrl, formData, ct);
        if (tokenResponse is null || string.IsNullOrEmpty(tokenResponse.AccessToken))
        {
            _logger.LogError("OAuth token refresh failed for secret {SecretName}.", kvSecretName);
            return null;
        }

        var updatedCreds = creds with
        {
            AccessToken = tokenResponse.AccessToken,
            RefreshToken = tokenResponse.RefreshToken ?? creds.RefreshToken,
            ExpiresAt = now.AddSeconds(tokenResponse.ExpiresIn > 0 ? tokenResponse.ExpiresIn : 3600),
        };

        var credJson = JsonSerializer.Serialize(updatedCreds, SharedJsonOptions.CamelCaseWrite);
        await _secretProvider.SetSecretAsync(kvSecretName, credJson, ct);

        _logger.LogInformation("OAuth token refreshed for secret {SecretName} (expires at {ExpiresAt}).",
            kvSecretName, updatedCreds.ExpiresAt);

        return updatedCreds.AccessToken;
    }

    // --- Provider-specific configuration ---

    internal static (string AuthorizeUrl, string ClientId, string Scopes) GetProviderConfig(
        ConnectorType connectorType, string? sourceConfig)
    {
        return connectorType switch
        {
            ConnectorType.HubSpot => (
                "https://app.hubspot.com/oauth/authorize",
                GetJsonField(sourceConfig, "oAuthClientId") ?? "",
                GetJsonField(sourceConfig, "oAuthScopes") ?? "crm.objects.contacts.read crm.objects.deals.read tickets"),

            ConnectorType.ClickUp => (
                "https://app.clickup.com/api",
                GetJsonField(sourceConfig, "oAuthClientId") ?? "",
                ""),

            ConnectorType.AzureDevOps => (
                "https://app.vssps.visualstudio.com/oauth2/authorize",
                GetJsonField(sourceConfig, "oAuthClientId") ?? "",
                GetJsonField(sourceConfig, "oAuthScopes") ?? "vso.work_full"),

            ConnectorType.SharePoint => (
                $"https://login.microsoftonline.com/{GetJsonField(sourceConfig, "entraIdTenantId") ?? "common"}/oauth2/v2.0/authorize",
                GetJsonField(sourceConfig, "oAuthClientId") ?? GetJsonField(sourceConfig, "clientId") ?? "",
                GetJsonField(sourceConfig, "oAuthScopes") ?? "https://graph.microsoft.com/.default offline_access"),

            _ => throw new NotSupportedException($"OAuth is not supported for connector type '{connectorType}'."),
        };
    }

    internal static string GetTokenUrl(ConnectorType connectorType, string? sourceConfig)
    {
        return connectorType switch
        {
            ConnectorType.HubSpot => "https://api.hubapi.com/oauth/v1/token",
            ConnectorType.ClickUp => "https://api.clickup.com/api/v2/oauth/token",
            ConnectorType.AzureDevOps => "https://app.vssps.visualstudio.com/oauth2/token",
            ConnectorType.SharePoint =>
                $"https://login.microsoftonline.com/{GetJsonField(sourceConfig, "entraIdTenantId") ?? "common"}/oauth2/v2.0/token",
            _ => throw new NotSupportedException($"OAuth is not supported for connector type '{connectorType}'."),
        };
    }

    // --- Helpers ---

    private string BuildRedirectUri(Guid connectorId)
    {
        var baseUrl = _settings.CallbackBaseUrl.TrimEnd('/');
        return $"{baseUrl}/api/admin/connectors/{connectorId}/oauth/callback";
    }

    internal string GenerateState(Guid connectorId, string tenantId)
    {
        var now = _timeProvider.GetUtcNow();
        var payload = $"{connectorId}|{tenantId}|{now.Ticks}";
        var payloadBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
        var signature = ComputeHmac(payloadBase64);
        return $"{payloadBase64}.{signature}";
    }

    private string ComputeHmac(string data)
    {
        var keyBytes = Convert.FromBase64String(_settings.StateSigningKey);
        var dataBytes = Encoding.UTF8.GetBytes(data);
        var hash = HMACSHA256.HashData(keyBytes, dataBytes);
        return Convert.ToBase64String(hash);
    }

    private async Task<OAuthTokenEndpointResponse?> PostTokenRequestAsync(
        string tokenUrl, Dictionary<string, string> formData, CancellationToken ct)
    {
        try
        {
            using var client = _httpClientFactory.CreateClient(HttpClientNames.OAuth);
            using var content = new FormUrlEncodedContent(formData);
            using var response = await client.PostAsync(tokenUrl, content, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("OAuth token request to {TokenUrl} failed with {StatusCode}: {Body}",
                    tokenUrl, response.StatusCode, errorBody);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<OAuthTokenEndpointResponse>(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "OAuth token request to {TokenUrl} threw an exception.", tokenUrl);
            return null;
        }
    }

    private string? GetJsonField(string? json, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(fieldName, out var val) ? val.GetString() : null;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse JSON when extracting field '{FieldName}'", fieldName);
            return null;
        }
    }
}
