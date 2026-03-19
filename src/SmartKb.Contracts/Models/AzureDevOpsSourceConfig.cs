namespace SmartKb.Contracts.Models;

/// <summary>
/// Source configuration for an Azure DevOps connector.
/// Stored as JSON in ConnectorEntity.SourceConfig.
/// </summary>
public sealed record AzureDevOpsSourceConfig
{
    /// <summary>
    /// ADO organization URL, e.g. "https://dev.azure.com/myorg".
    /// </summary>
    public required string OrganizationUrl { get; init; }

    /// <summary>
    /// Project names to ingest from. If empty, all accessible projects are used.
    /// </summary>
    public IReadOnlyList<string> Projects { get; init; } = [];

    /// <summary>
    /// Whether to ingest work items (bugs, tasks, user stories, etc.).
    /// </summary>
    public bool IngestWorkItems { get; init; } = true;

    /// <summary>
    /// Whether to ingest wiki pages.
    /// </summary>
    public bool IngestWikiPages { get; init; } = true;

    /// <summary>
    /// Work item types to include. If empty, all types are ingested.
    /// </summary>
    public IReadOnlyList<string> WorkItemTypes { get; init; } = [];

    /// <summary>
    /// Area paths to filter work items. If empty, all area paths are included.
    /// </summary>
    public IReadOnlyList<string> AreaPaths { get; init; } = [];

    /// <summary>
    /// Batch size for API pagination. Default 200 (ADO max is 200 for WIQL).
    /// </summary>
    public int BatchSize { get; init; } = 200;

    /// <summary>
    /// OAuth app client ID (required when AuthType is OAuth).
    /// </summary>
    public string? OAuthClientId { get; init; }

    /// <summary>
    /// OAuth scopes to request. Defaults to vso.work_full.
    /// </summary>
    public string? OAuthScopes { get; init; }
}
