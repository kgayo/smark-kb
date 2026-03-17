using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

/// <summary>
/// Interface for connectors that support creating external work items/tasks from escalation drafts.
/// Only ADO and ClickUp implement this; other connectors (SharePoint, HubSpot) are ingestion-only.
/// </summary>
public interface IEscalationTargetConnector
{
    ConnectorType Type { get; }

    Task<ExternalWorkItemResult> CreateExternalWorkItemAsync(
        string sourceConfig,
        string secretValue,
        ExternalWorkItemRequest request,
        CancellationToken ct = default);
}

/// <summary>
/// Request to create a work item/task in an external system from an escalation draft.
/// </summary>
public sealed record ExternalWorkItemRequest
{
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string Severity { get; init; }
    public string? TargetProject { get; init; }
    public string? TargetListId { get; init; }
    public string? AreaPath { get; init; }
    public string? WorkItemType { get; init; }
}

/// <summary>
/// Result of creating a work item/task in an external system.
/// </summary>
public sealed record ExternalWorkItemResult
{
    public required bool Success { get; init; }
    public string? ExternalId { get; init; }
    public string? ExternalUrl { get; init; }
    public string? ErrorDetail { get; init; }
}
