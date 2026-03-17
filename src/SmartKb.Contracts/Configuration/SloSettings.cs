namespace SmartKb.Contracts.Configuration;

/// <summary>
/// SLO (Service Level Objective) settings for monitoring and alerting.
/// Configurable via "Slo:*" app settings.
/// </summary>
public sealed class SloSettings
{
    public const string SectionName = "Slo";

    /// <summary>P95 answer-ready latency target in milliseconds. Default: 8000ms (8s).</summary>
    public int AnswerLatencyP95TargetMs { get; set; } = 8000;

    /// <summary>Availability target as a percentage. Default: 99.5%.</summary>
    public double AvailabilityTargetPercent { get; set; } = 99.5;

    /// <summary>P95 sync lag target in minutes. Default: 15 minutes.</summary>
    public int SyncLagP95TargetMinutes { get; set; } = 15;

    /// <summary>No-evidence rate threshold (0.0-1.0). Alert when exceeded. Default: 0.25 (25%).</summary>
    public double NoEvidenceRateThreshold { get; set; } = 0.25;

    /// <summary>Dead-letter queue depth threshold. Alert when exceeded in a 15-min window. Default: 10.</summary>
    public int DeadLetterDepthThreshold { get; set; } = 10;

    /// <summary>Evaluation window for metric alerts in minutes. Default: 5.</summary>
    public int AlertEvaluationWindowMinutes { get; set; } = 5;

    /// <summary>PII redaction spike threshold (count per hour). Default: 50.</summary>
    public int PiiRedactionSpikeThreshold { get; set; } = 50;
}
