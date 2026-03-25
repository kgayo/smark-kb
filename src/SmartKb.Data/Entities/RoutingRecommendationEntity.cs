using SmartKb.Contracts.Models;

namespace SmartKb.Data.Entities;

/// <summary>
/// Stores a routing improvement recommendation generated from outcome analysis.
/// Admins review and optionally apply recommendations to update routing rules.
/// </summary>
public sealed class RoutingRecommendationEntity
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Type of recommendation: TeamChange, ThresholdAdjust, NewRule.</summary>
    public string RecommendationType { get; set; } = string.Empty;

    public string ProductArea { get; set; } = string.Empty;
    public string CurrentTargetTeam { get; set; } = string.Empty;
    public string? SuggestedTargetTeam { get; set; }
    public float? CurrentThreshold { get; set; }
    public float? SuggestedThreshold { get; set; }

    /// <summary>Human-readable explanation of why this recommendation was generated.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Confidence score (0-1) based on outcome volume and consistency.</summary>
    public float Confidence { get; set; }

    /// <summary>Number of outcome events that support this recommendation.</summary>
    public int SupportingOutcomeCount { get; set; }

    /// <summary>Status: Pending, Applied, Dismissed.</summary>
    public string Status { get; set; } = WorkflowStatus.Pending;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? AppliedAt { get; set; }
    public string? AppliedBy { get; set; }
    public DateTimeOffset? DismissedAt { get; set; }
    public string? DismissedBy { get; set; }

    /// <summary>Optional FK to the eval report that triggered this recommendation (P3-023).</summary>
    public Guid? SourceEvalReportId { get; set; }

    public TenantEntity Tenant { get; set; } = null!;
    public EvalReportEntity? SourceEvalReport { get; set; }
}
