namespace SmartKb.Data.Entities;

/// <summary>
/// Records a detected contradiction between two case patterns — same/similar problem domain
/// but conflicting resolution steps. Requires human review to resolve.
/// P2-004: Pattern maintenance automation and contradiction detection.
/// </summary>
public sealed class PatternContradictionEntity
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;

    /// <summary>First pattern in the contradiction pair.</summary>
    public string PatternIdA { get; set; } = string.Empty;

    /// <summary>Second pattern in the contradiction pair.</summary>
    public string PatternIdB { get; set; } = string.Empty;

    /// <summary>Type of contradiction: ResolutionConflict, SymptomOverlap, DuplicatePattern.</summary>
    public string ContradictionType { get; set; } = string.Empty;

    /// <summary>Similarity score between the two patterns' problem/symptom domains (0-1).</summary>
    public float SimilarityScore { get; set; }

    /// <summary>Human-readable explanation of the detected contradiction.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Specific fields or steps that conflict.</summary>
    public string ConflictingFieldsJson { get; set; } = "[]";

    /// <summary>Status: Pending, Resolved, Dismissed.</summary>
    public string Status { get; set; } = "Pending";

    /// <summary>How the contradiction was resolved: Merged, Deprecated, Kept, Dismissed.</summary>
    public string? Resolution { get; set; }

    public string? ResolvedBy { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public string? ResolutionNotes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public TenantEntity Tenant { get; set; } = null!;
}
