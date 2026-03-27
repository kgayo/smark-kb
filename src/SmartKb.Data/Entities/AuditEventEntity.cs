namespace SmartKb.Data.Entities;

public sealed class AuditEventEntity
{
    public Guid Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>Unix epoch seconds of <see cref="Timestamp"/>. Enables server-side filtering in SQLite (which cannot compare DateTimeOffset).</summary>
    public long TimestampEpoch { get; set; }

    public string Detail { get; set; } = string.Empty;
}
