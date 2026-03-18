namespace SmartKb.Contracts.Models;

/// <summary>
/// Request to create an escalation handoff draft from a chat response with an escalation signal.
/// </summary>
public sealed record CreateEscalationDraftRequest
{
    public required Guid SessionId { get; init; }
    public required Guid MessageId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string CustomerSummary { get; init; } = string.Empty;
    public string StepsToReproduce { get; init; } = string.Empty;
    public string LogsIdsRequested { get; init; } = string.Empty;
    public string SuspectedComponent { get; init; } = string.Empty;
    public string Severity { get; init; } = "P3";
    public IReadOnlyList<CitationDto> EvidenceLinks { get; init; } = [];
    public string TargetTeam { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}

/// <summary>
/// Request to update an existing escalation draft (agent review/edit before export).
/// </summary>
public sealed record UpdateEscalationDraftRequest
{
    public string? Title { get; init; }
    public string? CustomerSummary { get; init; }
    public string? StepsToReproduce { get; init; }
    public string? LogsIdsRequested { get; init; }
    public string? SuspectedComponent { get; init; }
    public string? Severity { get; init; }
    public IReadOnlyList<CitationDto>? EvidenceLinks { get; init; }
    public string? TargetTeam { get; init; }
    public string? Reason { get; init; }
}

/// <summary>
/// Escalation handoff draft response with all structured fields for agent review.
/// </summary>
public sealed record EscalationDraftResponse
{
    public required Guid DraftId { get; init; }
    public required Guid SessionId { get; init; }
    public required Guid MessageId { get; init; }
    public required string Title { get; init; }
    public required string CustomerSummary { get; init; }
    public required string StepsToReproduce { get; init; }
    public required string LogsIdsRequested { get; init; }
    public required string SuspectedComponent { get; init; }
    public required string Severity { get; init; }
    public required IReadOnlyList<CitationDto> EvidenceLinks { get; init; }
    public required string TargetTeam { get; init; }
    public required string Reason { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ExportedAt { get; init; }
    // External creation tracking (P1-003).
    public DateTimeOffset? ApprovedAt { get; init; }
    public string? ExternalId { get; init; }
    public string? ExternalUrl { get; init; }
    public string? ExternalStatus { get; init; }
    public string? ExternalErrorDetail { get; init; }
    public string? TargetConnectorType { get; init; }
    /// <summary>Playbook validation result for the target team (P2-002). Null if no playbook exists.</summary>
    public PlaybookValidationResult? PlaybookValidation { get; init; }
}

/// <summary>
/// List of escalation drafts for a session.
/// </summary>
public sealed record EscalationDraftListResponse
{
    public required Guid SessionId { get; init; }
    public required IReadOnlyList<EscalationDraftResponse> Drafts { get; init; }
    public required int TotalCount { get; init; }
}

/// <summary>
/// Markdown export of an escalation draft.
/// </summary>
public sealed record EscalationDraftExportResponse
{
    public required Guid DraftId { get; init; }
    public required string Markdown { get; init; }
    public required DateTimeOffset ExportedAt { get; init; }
}

/// <summary>
/// Request to approve an escalation draft and create an external work item/task.
/// Agent must select a target connector (ADO or ClickUp) that has been configured in the admin panel.
/// </summary>
public sealed record ApproveEscalationDraftRequest
{
    public required Guid ConnectorId { get; init; }
    /// <summary>ADO project name. Optional — falls back to first configured project.</summary>
    public string? TargetProject { get; init; }
    /// <summary>ClickUp list ID. Optional — falls back to first configured/resolved list.</summary>
    public string? TargetListId { get; init; }
    /// <summary>ADO area path. Optional.</summary>
    public string? AreaPath { get; init; }
    /// <summary>ADO work item type (Bug, Task, etc.). Defaults to Bug.</summary>
    public string? WorkItemType { get; init; }
}

/// <summary>
/// Response after approving an escalation draft and creating an external work item/task.
/// </summary>
public sealed record ExternalEscalationResult
{
    public required Guid DraftId { get; init; }
    public required string ExternalStatus { get; init; }
    public string? ExternalId { get; init; }
    public string? ExternalUrl { get; init; }
    public string? ErrorDetail { get; init; }
    public DateTimeOffset? ApprovedAt { get; init; }
    public string? ConnectorType { get; init; }
}
