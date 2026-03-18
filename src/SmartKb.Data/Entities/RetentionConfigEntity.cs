namespace SmartKb.Data.Entities;

public sealed class RetentionConfigEntity
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public int RetentionDays { get; set; }
    public int? MetricRetentionDays { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public TenantEntity Tenant { get; set; } = null!;
}
