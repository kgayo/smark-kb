namespace SmartKb.Contracts.Configuration;

public sealed class ScheduledSyncSettings
{
    public const string SectionName = "ScheduledSync";

    /// <summary>
    /// How often the scheduler evaluates cron expressions (in seconds). Default: 60.
    /// </summary>
    public int EvaluationIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// Whether scheduled sync is enabled. Default: true.
    /// </summary>
    public bool Enabled { get; set; } = true;
}
