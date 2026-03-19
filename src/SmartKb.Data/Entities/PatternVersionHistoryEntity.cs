namespace SmartKb.Data.Entities;

/// <summary>
/// P3-013: Tracks field-level changes across pattern versions.
/// Each row records a single governance transition or content update,
/// capturing which fields changed and their previous values.
/// </summary>
public sealed class PatternVersionHistoryEntity
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string PatternId { get; set; } = string.Empty;
    public int Version { get; set; }
    public string ChangedBy { get; set; } = string.Empty;
    public DateTimeOffset ChangedAt { get; set; }

    /// <summary>JSON array of field names that were changed (e.g., ["TrustLevel","ReviewedBy"]).</summary>
    public string ChangedFieldsJson { get; set; } = "[]";

    /// <summary>JSON object mapping field name → previous value before the change.</summary>
    public string PreviousValuesJson { get; set; } = "{}";

    /// <summary>The type of change: "trust_transition", "content_update", etc.</summary>
    public string ChangeType { get; set; } = string.Empty;

    /// <summary>Optional summary describing the change (e.g., "Draft → Reviewed").</summary>
    public string? Summary { get; set; }
}
