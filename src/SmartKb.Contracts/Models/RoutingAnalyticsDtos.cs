namespace SmartKb.Contracts.Models;

/// <summary>
/// Per-team routing quality metrics computed from outcome data.
/// </summary>
public sealed record TeamRoutingMetrics
{
    public required string TargetTeam { get; init; }
    public required int TotalEscalations { get; init; }
    public required int AcceptedCount { get; init; }
    public required int ReroutedCount { get; init; }
    public required int PendingCount { get; init; }
    public required float AcceptanceRate { get; init; }
    public required float RerouteRate { get; init; }
    public TimeSpan? AvgTimeToAssign { get; init; }
    public TimeSpan? AvgTimeToResolve { get; init; }
}

/// <summary>
/// Per-product-area routing quality metrics.
/// </summary>
public sealed record ProductAreaRoutingMetrics
{
    public required string ProductArea { get; init; }
    public required string CurrentTargetTeam { get; init; }
    public required int TotalEscalations { get; init; }
    public required int AcceptedCount { get; init; }
    public required int ReroutedCount { get; init; }
    public required float AcceptanceRate { get; init; }
    public required float RerouteRate { get; init; }
}

/// <summary>
/// Tenant-wide routing analytics summary.
/// </summary>
public sealed record RoutingAnalyticsSummary
{
    public required string TenantId { get; init; }
    public required int TotalOutcomes { get; init; }
    public required int TotalEscalations { get; init; }
    public required int TotalReroutes { get; init; }
    public required int TotalResolvedWithoutEscalation { get; init; }
    public required float OverallAcceptanceRate { get; init; }
    public required float OverallRerouteRate { get; init; }
    public required float SelfResolutionRate { get; init; }
    public required IReadOnlyList<TeamRoutingMetrics> TeamMetrics { get; init; }
    public required IReadOnlyList<ProductAreaRoutingMetrics> ProductAreaMetrics { get; init; }
    public required DateTimeOffset ComputedAt { get; init; }
    public DateTimeOffset? WindowStart { get; init; }
    public DateTimeOffset? WindowEnd { get; init; }
}

/// <summary>
/// A routing improvement recommendation generated from outcome analysis.
/// </summary>
public sealed record RoutingRecommendationDto
{
    public required Guid RecommendationId { get; init; }
    public required string RecommendationType { get; init; }
    public required string ProductArea { get; init; }
    public required string CurrentTargetTeam { get; init; }
    public string? SuggestedTargetTeam { get; init; }
    public float? CurrentThreshold { get; init; }
    public float? SuggestedThreshold { get; init; }
    public required string Reason { get; init; }
    public required float Confidence { get; init; }
    public required int SupportingOutcomeCount { get; init; }
    public required string Status { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? AppliedAt { get; init; }
    public string? AppliedBy { get; init; }

    /// <summary>Optional eval report that triggered this recommendation (P3-023).</summary>
    public Guid? SourceEvalReportId { get; init; }
}

public sealed record RoutingRecommendationListResponse
{
    public required IReadOnlyList<RoutingRecommendationDto> Recommendations { get; init; }
    public required int TotalCount { get; init; }
}

/// <summary>
/// Request to generate routing recommendations, optionally linked to an eval report (P3-023).
/// </summary>
public sealed record GenerateRecommendationsRequest
{
    /// <summary>Optional eval report ID to link generated recommendations to.</summary>
    public Guid? SourceEvalReportId { get; init; }
}

/// <summary>
/// Request to apply a routing recommendation (admin action).
/// </summary>
public sealed record ApplyRecommendationRequest
{
    public string? OverrideTargetTeam { get; init; }
    public float? OverrideThreshold { get; init; }
}

/// <summary>
/// Routing rule DTO for admin CRUD.
/// </summary>
public sealed record RoutingRuleDto
{
    public required Guid RuleId { get; init; }
    public required string ProductArea { get; init; }
    public required string TargetTeam { get; init; }
    public required float EscalationThreshold { get; init; }
    public required string MinSeverity { get; init; }
    public required bool IsActive { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}

public sealed record RoutingRuleListResponse
{
    public required IReadOnlyList<RoutingRuleDto> Rules { get; init; }
    public required int TotalCount { get; init; }
}

public sealed record CreateRoutingRuleRequest
{
    public required string ProductArea { get; init; }
    public required string TargetTeam { get; init; }
    public float EscalationThreshold { get; init; } = 0.4f;
    public string MinSeverity { get; init; } = "P2";
}

public sealed record UpdateRoutingRuleRequest
{
    public string? TargetTeam { get; init; }
    public float? EscalationThreshold { get; init; }
    public string? MinSeverity { get; init; }
    public bool? IsActive { get; init; }
}
