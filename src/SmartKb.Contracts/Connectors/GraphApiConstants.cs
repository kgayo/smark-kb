namespace SmartKb.Contracts.Connectors;

/// <summary>
/// Shared Microsoft Graph API URL constants used by SharePoint connector client and webhook manager.
/// </summary>
internal static class GraphApiConstants
{
    internal const string BaseUrl = "https://graph.microsoft.com/v1.0";
    internal const string TokenUrl = "https://login.microsoftonline.com/{0}/oauth2/v2.0/token";
}
