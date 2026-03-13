namespace SmartKb.Data.Entities;

public sealed class AuditEventEntity
{
    public Guid Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public string Detail { get; set; } = string.Empty;
}
