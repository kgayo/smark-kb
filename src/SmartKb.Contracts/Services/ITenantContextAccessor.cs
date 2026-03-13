using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

public interface ITenantContextAccessor
{
    TenantContext? Current { get; set; }
}
