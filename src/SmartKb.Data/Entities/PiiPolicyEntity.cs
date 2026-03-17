namespace SmartKb.Data.Entities;

/// <summary>
/// Per-tenant PII policy configuration: which PII types to detect/redact,
/// enforcement mode, and optional custom regex patterns (P2-001).
/// </summary>
public sealed class PiiPolicyEntity
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Enforcement mode: "redact" (replace with placeholders), "detect" (flag only, no masking), "disabled" (skip PII checks).</summary>
    public string EnforcementMode { get; set; } = "redact";

    /// <summary>Comma-separated list of enabled built-in PII types: email,phone,ssn,credit_card.</summary>
    public string EnabledPiiTypes { get; set; } = "email,phone,ssn,credit_card";

    /// <summary>JSON array of custom regex patterns: [{"name":"order_id","pattern":"ORD-\\d{8}","placeholder":"[REDACTED-ORDER-ID]"}].</summary>
    public string CustomPatternsJson { get; set; } = "[]";

    /// <summary>Whether to include redaction details in audit events.</summary>
    public bool AuditRedactions { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public TenantEntity Tenant { get; set; } = null!;
}
