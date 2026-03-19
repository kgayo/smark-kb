namespace SmartKb.Contracts.Models;

/// <summary>
/// Source configuration for a HubSpot connector.
/// Stored as JSON in ConnectorEntity.SourceConfig.
/// </summary>
public sealed record HubSpotSourceConfig
{
    /// <summary>
    /// HubSpot portal ID (required for deep links and API routing).
    /// </summary>
    public required string PortalId { get; init; }

    /// <summary>
    /// HubSpot API base URL. Override for testing/proxy.
    /// </summary>
    public string BaseUrl { get; init; } = "https://api.hubapi.com";

    /// <summary>
    /// Object types to ingest: "tickets", "contacts", "companies", "deals".
    /// Defaults to tickets only.
    /// </summary>
    public IReadOnlyList<string> ObjectTypes { get; init; } = ["tickets"];

    /// <summary>
    /// Additional properties to request from HubSpot CRM objects API.
    /// If empty, uses default properties per object type.
    /// </summary>
    public IReadOnlyList<string> CustomProperties { get; init; } = [];

    /// <summary>
    /// Pipeline IDs to filter (applies to tickets and deals). If empty, ingest all.
    /// </summary>
    public IReadOnlyList<string> Pipelines { get; init; } = [];

    /// <summary>
    /// Batch size for API pagination. HubSpot max is 100.
    /// </summary>
    public int BatchSize { get; init; } = 100;

    /// <summary>
    /// OAuth app client ID (required when AuthType is OAuth).
    /// </summary>
    public string? OAuthClientId { get; init; }

    /// <summary>
    /// OAuth scopes to request. Defaults to HubSpot CRM read + tickets scopes.
    /// </summary>
    public string? OAuthScopes { get; init; }
}
