namespace SmartKb.Contracts.Connectors;

/// <summary>
/// Shared Microsoft Graph API URL and OAuth constants used by SharePoint connector client,
/// webhook manager, and OAuthTokenService.
/// </summary>
internal static class GraphApiConstants
{
    internal const string BaseUrl = "https://graph.microsoft.com/v1.0";
    internal const string TokenUrl = "https://login.microsoftonline.com/{0}/oauth2/v2.0/token";
    internal const string DefaultScope = "https://graph.microsoft.com/.default";
    internal const string ClientCredentialsGrantType = "client_credentials";
}
