namespace SmartKb.Contracts.Configuration;

/// <summary>
/// Configuration for routing analytics and improvement recommendations.
/// </summary>
public sealed class RoutingAnalyticsSettings
{
    public const string SectionName = "RoutingAnalytics";

    /// <summary>Default analytics window in days (how far back to look for outcomes).</summary>
    public int DefaultWindowDays { get; set; } = 30;

    /// <summary>Minimum outcomes required before generating recommendations.</summary>
    public int MinOutcomesForRecommendation { get; set; } = 5;

    /// <summary>Reroute rate threshold above which a team-change recommendation is generated.</summary>
    public float RerouteRateThreshold { get; set; } = 0.3f;

    /// <summary>Acceptance rate threshold below which a threshold-adjustment recommendation is generated.</summary>
    public float LowAcceptanceRateThreshold { get; set; } = 0.5f;

    /// <summary>Minimum confidence for a recommendation to be actionable.</summary>
    public float MinRecommendationConfidence { get; set; } = 0.5f;
}
