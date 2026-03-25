namespace SmartKb.Contracts;

/// <summary>
/// Named HTTP client identifiers used in IHttpClientFactory registration and resolution.
/// </summary>
public static class HttpClientNames
{
    public const string AzureDevOps = "AzureDevOps";
    public const string SharePoint = "SharePoint";
    public const string HubSpot = "HubSpot";
    public const string ClickUp = "ClickUp";
    public const string OpenAi = "OpenAi";
    public const string OAuth = "oauth";
    public const string EvalNotification = "EvalNotification";
}
