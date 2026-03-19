namespace SmartKb.Data.Entities;

public sealed class RateLimitEventEntity
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public Guid ConnectorId { get; set; }
    public string ConnectorType { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }

    public ConnectorEntity? Connector { get; set; }
}
