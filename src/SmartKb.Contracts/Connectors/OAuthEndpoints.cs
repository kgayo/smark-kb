namespace SmartKb.Contracts.Connectors;

/// <summary>
/// Shared OAuth authorization and token endpoint URLs for external connector providers.
/// </summary>
internal static class OAuthEndpoints
{
    // --- HubSpot ---
    internal const string HubSpotAuthorizeUrl = "https://app.hubspot.com/oauth/authorize";
    internal const string HubSpotTokenUrl = "https://api.hubapi.com/oauth/v1/token";

    // --- ClickUp ---
    internal const string ClickUpAuthorizeUrl = "https://app.clickup.com/api";
    internal const string ClickUpTokenUrl = "https://api.clickup.com/api/v2/oauth/token";

    // --- Azure DevOps ---
    internal const string AzureDevOpsAuthorizeUrl = "https://app.vssps.visualstudio.com/oauth2/authorize";
    internal const string AzureDevOpsTokenUrl = "https://app.vssps.visualstudio.com/oauth2/token";

    // --- SharePoint / Entra ID ---
    internal const string EntraIdAuthorizeUrlTemplate = "https://login.microsoftonline.com/{0}/oauth2/v2.0/authorize";
    internal const string EntraIdTokenUrlTemplate = "https://login.microsoftonline.com/{0}/oauth2/v2.0/token";
    internal const string EntraIdDefaultTenant = "common";

    /// <summary>
    /// Default token expiry in seconds when the provider does not supply an expires_in value.
    /// </summary>
    internal const int DefaultTokenExpirySeconds = 3600;
}
