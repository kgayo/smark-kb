using System.Text.Json.Serialization;

namespace SmartKb.Contracts.Models;

/// <summary>
/// OAuth credentials stored as JSON in Key Vault when AuthType == OAuth.
/// Contains both the client credentials and the token pair.
/// </summary>
public sealed record OAuthCredentials
{
    [JsonPropertyName("client_id")]
    public string ClientId { get; init; } = "";

    [JsonPropertyName("client_secret")]
    public string ClientSecret { get; init; } = "";

    [JsonPropertyName("access_token")]
    public string? AccessToken { get; init; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; init; }

    [JsonPropertyName("expires_at")]
    public DateTimeOffset? ExpiresAt { get; init; }
}

/// <summary>
/// Response from an OAuth provider's token endpoint.
/// </summary>
internal sealed record OAuthTokenEndpointResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; init; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; init; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; init; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; init; }
}

/// <summary>
/// Response DTO for the OAuth authorize URL endpoint.
/// </summary>
public sealed record OAuthAuthorizeUrlResponse
{
    public required string AuthorizeUrl { get; init; }
}

/// <summary>
/// Response DTO for the OAuth callback endpoint.
/// </summary>
public sealed record OAuthCallbackResponse
{
    public required bool Success { get; init; }
    public required string Message { get; init; }
}
