namespace SmartKb.Contracts.Configuration;

public sealed class SecretRotationSettings
{
    public const string SectionName = "SecretRotation";

    /// <summary>Days before expiry to surface a warning.</summary>
    public int WarningThresholdDays { get; set; } = 30;

    /// <summary>Days before expiry to surface a critical alert.</summary>
    public int CriticalThresholdDays { get; set; } = 7;

    /// <summary>Maximum age in days for secrets without explicit expiry. 0 = no age-based warnings.</summary>
    public int MaxAgeDays { get; set; } = 90;
}
