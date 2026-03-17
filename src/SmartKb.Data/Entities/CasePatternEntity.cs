namespace SmartKb.Data.Entities;

/// <summary>
/// SQL entity for a case pattern — a distilled playbook from solved tickets.
/// Stored in SQL for governance and persistence; indexed in Azure AI Search Pattern index
/// for retrieval fusion with the Evidence Store.
/// </summary>
public sealed class CasePatternEntity
{
    public Guid Id { get; set; }
    public string PatternId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ProblemStatement { get; set; } = string.Empty;
    public string SymptomsJson { get; set; } = "[]";
    public string DiagnosisStepsJson { get; set; } = "[]";
    public string ResolutionStepsJson { get; set; } = "[]";
    public string VerificationStepsJson { get; set; } = "[]";
    public string? Workaround { get; set; }
    public string EscalationCriteriaJson { get; set; } = "[]";
    public string? EscalationTargetTeam { get; set; }
    public string RelatedEvidenceIdsJson { get; set; } = "[]";
    public float Confidence { get; set; }
    public string TrustLevel { get; set; } = "Draft";
    public int Version { get; set; } = 1;
    public string? SupersedesPatternId { get; set; }
    public string ApplicabilityConstraintsJson { get; set; } = "[]";
    public string ExclusionsJson { get; set; } = "[]";
    public string? ProductArea { get; set; }
    public string TagsJson { get; set; } = "[]";
    public string Visibility { get; set; } = "Internal";
    public string AllowedGroupsJson { get; set; } = "[]";
    public string AccessLabel { get; set; } = "Internal";
    public string SourceUrl { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    // Navigation
    public TenantEntity Tenant { get; set; } = null!;
}
