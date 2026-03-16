namespace SmartKb.Data.Entities;

/// <summary>
/// Structured handoff draft created by an agent for review before escalation.
/// Phase 1: copy/export only (external ticket creation deferred to P1-003).
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

    public SessionEntity Session { get; set; } = null!;
}
