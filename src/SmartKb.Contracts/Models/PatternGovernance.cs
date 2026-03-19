using SmartKb.Contracts.Enums;

namespace SmartKb.Contracts.Models;

/// <summary>Request to transition a pattern to the Reviewed trust level.</summary>
public sealed record ReviewPatternRequest
{
    public string? Notes { get; init; }
}

/// <summary>Request to approve a pattern (transition to Approved trust level).</summary>
public sealed record ApprovePatternRequest
{
    public string? Notes { get; init; }
}

/// <summary>Request to deprecate a pattern.</summary>
public sealed record DeprecatePatternRequest
{
    public string? Reason { get; init; }

    /// <summary>Pattern ID that supersedes this one, if any.</summary>
    public string? SupersedingPatternId { get; init; }
}

/// <summary>Summary DTO for a pattern in the governance queue.</summary>
public sealed record PatternSummary
{
    public required Guid Id { get; init; }
    public required string PatternId { get; init; }
    public required string Title { get; init; }
    public required string ProblemStatement { get; init; }
    public required string TrustLevel { get; init; }
    public float Confidence { get; init; }
    public int Version { get; init; }
    public string? ProductArea { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public string? SupersedesPatternId { get; init; }
    public string SourceUrl { get; init; } = string.Empty;
    public int RelatedEvidenceCount { get; init; }
    public float? QualityScore { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }

    // Governance metadata.
    public string? ReviewedBy { get; init; }
    public DateTimeOffset? ReviewedAt { get; init; }
    public string? ApprovedBy { get; init; }
    public DateTimeOffset? ApprovedAt { get; init; }
    public string? DeprecatedBy { get; init; }
    public DateTimeOffset? DeprecatedAt { get; init; }
    public string? DeprecationReason { get; init; }
}

/// <summary>Full detail DTO for a single pattern.</summary>
public sealed record PatternDetail
{
    public required Guid Id { get; init; }
    public required string PatternId { get; init; }
    public required string TenantId { get; init; }
    public required string Title { get; init; }
    public required string ProblemStatement { get; init; }
    public string? RootCause { get; init; }
    public IReadOnlyList<string> Symptoms { get; init; } = [];
    public IReadOnlyList<string> DiagnosisSteps { get; init; } = [];
    public IReadOnlyList<string> ResolutionSteps { get; init; } = [];
    public IReadOnlyList<string> VerificationSteps { get; init; } = [];
    public string? Workaround { get; init; }
    public IReadOnlyList<string> EscalationCriteria { get; init; } = [];
    public string? EscalationTargetTeam { get; init; }
    public IReadOnlyList<string> RelatedEvidenceIds { get; init; } = [];
    public float Confidence { get; init; }
    public required string TrustLevel { get; init; }
    public int Version { get; init; }
    public string? SupersedesPatternId { get; init; }
    public IReadOnlyList<string> ApplicabilityConstraints { get; init; } = [];
    public IReadOnlyList<string> Exclusions { get; init; } = [];
    public string? ProductArea { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public string Visibility { get; init; } = "Internal";
    public string AccessLabel { get; init; } = "Internal";
    public string SourceUrl { get; init; } = string.Empty;
    public float? QualityScore { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }

    // Governance metadata.
    public string? ReviewedBy { get; init; }
    public DateTimeOffset? ReviewedAt { get; init; }
    public string? ReviewNotes { get; init; }
    public string? ApprovedBy { get; init; }
    public DateTimeOffset? ApprovedAt { get; init; }
    public string? ApprovalNotes { get; init; }
    public string? DeprecatedBy { get; init; }
    public DateTimeOffset? DeprecatedAt { get; init; }
    public string? DeprecationReason { get; init; }
}

/// <summary>Paginated response for the governance queue.</summary>
public sealed record PatternGovernanceQueueResponse
{
    public IReadOnlyList<PatternSummary> Patterns { get; init; } = [];
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public bool HasMore { get; init; }
}

/// <summary>Response after a governance transition (approve/deprecate/review).</summary>
public sealed record PatternGovernanceResult
{
    public required string PatternId { get; init; }
    public required string PreviousTrustLevel { get; init; }
    public required string NewTrustLevel { get; init; }
    public required string TransitionedBy { get; init; }
    public DateTimeOffset TransitionedAt { get; init; }
}
