namespace SmartKb.Data.Entities;

/// <summary>
/// Per-tenant escalation routing rule. Maps product area to target team with
/// configurable escalation threshold. D-004 resolution.
/// </summary>
public sealed class EscalationRoutingRuleEntity
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string ProductArea { get; set; } = string.Empty;
    public string TargetTeam { get; set; } = string.Empty;
    public float EscalationThreshold { get; set; } = 0.4f;
    public string MinSeverity { get; set; } = "P2";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public TenantEntity Tenant { get; set; } = null!;
}
