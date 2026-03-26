namespace SmartKb.Contracts.Connectors;

/// <summary>
/// Shared constants for connector deep-link URL templates and non-standard media types.
/// </summary>
internal static class ConnectorConstants
{
    // --- Deep-link URL templates ---

    /// <summary>ClickUp task deep-link. Format arg: task ID.</summary>
    internal const string ClickUpTaskDeepLinkTemplate = "https://app.clickup.com/t/{0}";

    /// <summary>HubSpot object deep-link. Format args: portal ID, URL path segment, object ID.</summary>
    internal const string HubSpotObjectDeepLinkTemplate = "https://app.hubspot.com/contacts/{0}/{1}/{2}";

    // --- Media types ---

    /// <summary>JSON Patch media type required by Azure DevOps PATCH APIs.</summary>
    internal const string JsonPatchMediaType = "application/json-patch+json";
}
