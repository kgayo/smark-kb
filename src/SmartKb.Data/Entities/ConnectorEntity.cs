using SmartKb.Contracts.Enums;

namespace SmartKb.Data.Entities;

public sealed class ConnectorEntity
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public ConnectorType ConnectorType { get; set; }
    public ConnectorStatus Status { get; set; }
    public SecretAuthType AuthType { get; set; }
    public string? KeyVaultSecretName { get; set; }
    public string? SourceConfig { get; set; }
    public string? FieldMapping { get; set; }
    public string? ScheduleCron { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public TenantEntity Tenant { get; set; } = null!;
    public ICollection<SyncRunEntity> SyncRuns { get; set; } = [];
}
