namespace SmartKb.Data.Entities;

public sealed class RetentionExecutionLogEntity
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public int DeletedCount { get; set; }
    public DateTimeOffset CutoffDate { get; set; }
    public DateTimeOffset ExecutedAt { get; set; }
    public long DurationMs { get; set; }
    public string ActorId { get; set; } = string.Empty;

    public TenantEntity Tenant { get; set; } = null!;
}
