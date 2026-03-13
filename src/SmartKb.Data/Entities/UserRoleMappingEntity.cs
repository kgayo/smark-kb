using SmartKb.Contracts.Enums;

namespace SmartKb.Data.Entities;

public sealed class UserRoleMappingEntity
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public AppRole Role { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public TenantEntity Tenant { get; set; } = null!;
}
