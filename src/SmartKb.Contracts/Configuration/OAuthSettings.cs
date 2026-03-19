namespace SmartKb.Contracts.Configuration;

/// <summary>
/// Configuration for OAuth authorization code flow support.
/// </summary>
public sealed class OAuthSettings
{
    /// <summary>
    /// Base URL for OAuth callback endpoints, e.g. "https://smartkb.example.com".
    /// Used to construct redirect_uri for provider authorization.
    /// </summary>
    public string CallbackBaseUrl { get; set; } = "";

    /// <summary>
    /// HMAC-SHA256 signing key for state parameter CSRF protection.
    /// Must be a base64-encoded 32+ byte key.
    /// </summary>
    public string StateSigningKey { get; set; } = "";

    public bool IsConfigured => !string.IsNullOrEmpty(CallbackBaseUrl) && !string.IsNullOrEmpty(StateSigningKey);
}
