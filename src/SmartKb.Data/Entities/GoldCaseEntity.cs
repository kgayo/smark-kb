namespace SmartKb.Data.Entities;

/// <summary>
/// Persisted gold dataset evaluation case for in-app management (P3-022).
/// </summary>
public sealed class GoldCaseEntity
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Gold case identifier, e.g. "eval-00001".</summary>
    public string CaseId { get; set; } = string.Empty;

    /// <summary>Natural-language query (min 5 chars).</summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>Serialized EvalContext JSON (nullable).</summary>
    public string? ContextJson { get; set; }

    /// <summary>Serialized EvalExpected JSON.</summary>
    public string ExpectedJson { get; set; } = string.Empty;

    /// <summary>Serialized string[] tags JSON.</summary>
    public string TagsJson { get; set; } = "[]";

    /// <summary>Optional: feedback ID this case was promoted from.</summary>
    public Guid? SourceFeedbackId { get; set; }

    /// <summary>User who created or last edited this case.</summary>
    public string CreatedBy { get; set; } = string.Empty;
    public string? UpdatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public TenantEntity Tenant { get; set; } = null!;
}
