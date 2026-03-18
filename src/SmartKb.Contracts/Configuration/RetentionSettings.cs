namespace SmartKb.Contracts.Configuration;

/// <summary>
/// Global retention execution settings (P2-005). Section: "Retention".
/// </summary>
public sealed class RetentionSettings
{
    public static readonly string SectionName = "Retention";

    /// <summary>Maximum number of days between cleanup executions before a policy is considered overdue.</summary>
    public int ComplianceWindowDays { get; set; } = 7;

    /// <summary>Maximum number of execution log entries to retain per tenant (oldest pruned first).</summary>
    public int MaxExecutionLogEntries { get; set; } = 1000;
}
