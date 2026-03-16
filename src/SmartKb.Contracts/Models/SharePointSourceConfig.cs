namespace SmartKb.Contracts.Models;

/// <summary>
/// Source configuration for a SharePoint connector.
/// Stored as JSON in ConnectorEntity.SourceConfig.
/// Uses Microsoft Graph API for delta queries and change notifications.
/// </summary>
public sealed record SharePointSourceConfig
{
    /// <summary>
    /// SharePoint site URL, e.g. "https://contoso.sharepoint.com/sites/support".
    /// Used to resolve the site ID via Graph.
    /// </summary>
    public required string SiteUrl { get; init; }

    /// <summary>
    /// Azure AD tenant ID for OAuth2 client credentials flow.
    /// This is the Entra ID tenant hosting the SharePoint site, NOT the SmartKB multi-tenant ID.
    /// </summary>
    public required string EntraIdTenantId { get; init; }

    /// <summary>
    /// Azure AD app registration client ID for Graph API access.
    /// </summary>
    public required string ClientId { get; init; }

    /// <summary>
    /// Specific drive IDs to ingest. If empty, all document libraries on the site are discovered.
    /// </summary>
    public IReadOnlyList<string> DriveIds { get; init; } = [];

    /// <summary>
    /// Whether to ingest document library files (default true).
    /// </summary>
    public bool IngestDocumentLibraries { get; init; } = true;

    /// <summary>
    /// File extensions to include (e.g. [".docx", ".pdf", ".md", ".txt"]).
    /// If empty, all supported text-extractable files are ingested.
    /// </summary>
    public IReadOnlyList<string> IncludeExtensions { get; init; } = [];

    /// <summary>
    /// Folder paths to exclude from ingestion (relative to drive root).
    /// </summary>
    public IReadOnlyList<string> ExcludeFolders { get; init; } = [];

    /// <summary>
    /// Batch size for delta query pagination. Default 200.
    /// </summary>
    public int BatchSize { get; init; } = 200;
}
