namespace SmartKb.Data.Entities;

/// <summary>
/// Per-tenant team playbook defining escalation requirements, SOP checklists,
/// and routing policies for a specific team. P2-002 implementation.
/// </summary>
public sealed class TeamPlaybookEntity
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string TeamName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>JSON array of required handoff field names beyond the standard set.</summary>
    public string RequiredFieldsJson { get; set; } = "[]";

    /// <summary>JSON array of SOP checklist items the agent should verify before escalating.</summary>
    public string ChecklistJson { get; set; } = "[]";

    /// <summary>Contact channel for the team (e.g. Slack channel, email DL).</summary>
    public string? ContactChannel { get; set; }

    /// <summary>Whether escalations to this team require lead/admin approval.</summary>
    public bool RequiresApproval { get; set; }

    /// <summary>Minimum severity required to escalate to this team (P1/P2/P3/P4). Null = any severity.</summary>
    public string? MinSeverity { get; set; }

    /// <summary>Severity that auto-suggests this team for immediate routing (e.g. P1 → on-call).</summary>
    public string? AutoRouteSeverity { get; set; }

    /// <summary>Maximum concurrent open escalations to this team. Null = unlimited.</summary>
    public int? MaxConcurrentEscalations { get; set; }

    /// <summary>Fallback team name when this team's max concurrent limit is reached.</summary>
    public string? FallbackTeam { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public TenantEntity Tenant { get; set; } = null!;
}
