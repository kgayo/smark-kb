namespace SmartKb.Contracts.Models;

/// <summary>
/// Source configuration for a ClickUp connector.
/// Stored as JSON in ConnectorEntity.SourceConfig.
/// </summary>
public sealed record ClickUpSourceConfig
{
    /// <summary>
    /// ClickUp workspace ID (required for API routing and deep links).
    /// </summary>
    public required string WorkspaceId { get; init; }

    /// <summary>
    /// ClickUp API base URL. Override for testing/proxy.
    /// </summary>
    public string BaseUrl { get; init; } = "https://api.clickup.com";

    /// <summary>
    /// Space IDs to ingest. If empty, ingests all spaces in the workspace.
    /// </summary>
    public IReadOnlyList<string> SpaceIds { get; init; } = [];

    /// <summary>
    /// Folder IDs to ingest. If empty, ingests all folders in selected spaces.
    /// </summary>
    public IReadOnlyList<string> FolderIds { get; init; } = [];

    /// <summary>
    /// List IDs to ingest. If empty, ingests all lists in selected folders/spaces.
    /// </summary>
    public IReadOnlyList<string> ListIds { get; init; } = [];

    /// <summary>
    /// Whether to ingest tasks. Defaults to true.
    /// </summary>
    public bool IngestTasks { get; init; } = true;

    /// <summary>
    /// Whether to ingest docs (ClickUp Docs). Defaults to true.
    /// </summary>
    public bool IngestDocs { get; init; } = true;

    /// <summary>
    /// Task statuses to include (e.g. "open", "in progress", "closed"). If empty, ingest all.
    /// </summary>
    public IReadOnlyList<string> TaskStatuses { get; init; } = [];

    /// <summary>
    /// Batch size for API pagination. ClickUp max is 100.
    /// </summary>
    public int BatchSize { get; init; } = 100;
}
