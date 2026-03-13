using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;

namespace SmartKb.Api.Tenant;

public sealed class TenantContextAccessor : ITenantContextAccessor
{
    public TenantContext? Current { get; set; }
}
