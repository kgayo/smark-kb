namespace SmartKb.Contracts.Connectors;

/// <summary>
/// Shared constants for connector deep-link URL templates, non-standard media types,
/// and Azure DevOps field/API names.
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

    // --- Azure DevOps API ---

    /// <summary>Azure DevOps REST API version used by connector client and webhook manager.</summary>
    internal const string AdoApiVersion = "7.1";

    // --- Azure DevOps work item field reference names ---

    internal const string AdoFieldId = "System.Id";
    internal const string AdoFieldTitle = "System.Title";
    internal const string AdoFieldDescription = "System.Description";
    internal const string AdoFieldWorkItemType = "System.WorkItemType";
    internal const string AdoFieldState = "System.State";
    internal const string AdoFieldAreaPath = "System.AreaPath";
    internal const string AdoFieldAssignedTo = "System.AssignedTo";
    internal const string AdoFieldCreatedDate = "System.CreatedDate";
    internal const string AdoFieldChangedDate = "System.ChangedDate";
    internal const string AdoFieldTags = "System.Tags";
    internal const string AdoFieldTeamProject = "System.TeamProject";
}
