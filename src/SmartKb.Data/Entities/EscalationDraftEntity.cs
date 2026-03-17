using SmartKb.Contracts.Enums;

namespace SmartKb.Data.Entities;

/// <summary>
/// Structured handoff draft created by an agent for review before escalation.
/// Supports copy/export and external ticket creation in ADO/ClickUp after human approval.
/// </summary>
public sealed class EscalationDraftEntity
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public Guid MessageId { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string CustomerSummary { get; set; } = string.Empty;
    public string StepsToReproduce { get; set; } = string.Empty;
    public string LogsIdsRequested { get; set; } = string.Empty;
    public string SuspectedComponent { get; set; } = string.Empty;
    public string Severity { get; set; } = "P3";
    public string EvidenceLinksJson { get; set; } = "[]";
    public string TargetTeam { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExportedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    // External creation tracking (P1-003).
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public Guid? TargetConnectorId { get; set; }
    public ConnectorType? TargetConnectorType { get; set; }
    public string? ExternalId { get; set; }
    public string? ExternalUrl { get; set; }
    public string? ExternalStatus { get; set; }
    public string? ExternalErrorDetail { get; set; }

    public SessionEntity Session { get; set; } = null!;
    public ConnectorEntity? TargetConnector { get; set; }
}
