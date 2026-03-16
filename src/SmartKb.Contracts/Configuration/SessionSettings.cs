namespace SmartKb.Contracts.Configuration;

public sealed class SessionSettings
{
    public const string SectionName = "Session";

    /// <summary>Default session expiry in hours. 0 = no expiry.</summary>
    public int DefaultExpiryHours { get; set; } = 24;

    /// <summary>Maximum number of messages per session (0 = unlimited).</summary>
    public int MaxMessagesPerSession { get; set; } = 200;

    /// <summary>Maximum number of active (non-expired) sessions per user (0 = unlimited).</summary>
    public int MaxActiveSessionsPerUser { get; set; } = 50;

    public bool HasExpiry => DefaultExpiryHours > 0;
}
