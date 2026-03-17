namespace SmartKb.Data.Entities;

public sealed class TenantEntity
{
    public string TenantId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<UserRoleMappingEntity> UserRoleMappings { get; set; } = [];
    public ICollection<ConnectorEntity> Connectors { get; set; } = [];
    public ICollection<SessionEntity> Sessions { get; set; } = [];
    public ICollection<RetentionConfigEntity> RetentionConfigs { get; set; } = [];
    public PiiPolicyEntity? PiiPolicy { get; set; }
}
