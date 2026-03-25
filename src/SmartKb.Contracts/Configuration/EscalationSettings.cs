namespace SmartKb.Contracts.Configuration;

/// <summary>
/// Escalation policy configuration. Resolves D-004:
/// - Confidence-based trigger: escalation recommended when confidence &lt; threshold AND severity >= min severity.
/// - Per-tenant routing via EscalationRoutingRules table.
/// - Global fallback team when no routing rule matches.
/// </summary>
public sealed class EscalationSettings
{
    public const string SectionName = "Escalation";

    /// <summary>Default confidence threshold below which escalation is recommended (D-004).</summary>
    public float DefaultConfidenceThreshold { get; set; } = 0.4f;

    /// <summary>Default minimum severity for escalation trigger (D-004: P2 or higher).</summary>
    public string DefaultMinSeverity { get; set; } = "P2";

    /// <summary>Global fallback target team when no per-tenant routing rule matches.</summary>
    public string FallbackTargetTeam { get; set; } = "Engineering";

    /// <summary>Severity ordering for comparison (lower index = higher severity).</summary>
    public static readonly string[] SeverityOrder = ["P1", "P2", "P3", "P4"];

    /// <summary>Returns true if <paramref name="severity"/> is at or above <paramref name="minSeverity"/>.</summary>
    public static bool MeetsSeverityThreshold(string severity, string minSeverity)
    {
        var severityIdx = Array.IndexOf(SeverityOrder, severity);
        var minIdx = Array.IndexOf(SeverityOrder, minSeverity);
        if (severityIdx < 0 || minIdx < 0) return false;
        return severityIdx <= minIdx;
    }

    /// <summary>Normalizes severity to uppercase and falls back to "P3" if unrecognized.</summary>
    public static string NormalizeSeverity(string severity)
    {
        var normalized = severity.ToUpperInvariant();
        return SeverityOrder.Contains(normalized) ? normalized : "P3";
    }

    /// <summary>Validates severity and throws <see cref="ArgumentException"/> if unrecognized.</summary>
    public static void ValidateSeverity(string severity)
    {
        if (!SeverityOrder.Contains(severity.ToUpperInvariant()))
            throw new ArgumentException($"Invalid severity: {severity}. Must be one of: {string.Join(", ", SeverityOrder)}");
    }
}
