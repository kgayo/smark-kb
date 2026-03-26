using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

/// <summary>
/// Extension methods for <see cref="ITenantContextAccessor"/> to eliminate null-forgiving operators.
/// </summary>
public static class TenantContextAccessorExtensions
{
    /// <summary>
    /// Returns the current <see cref="TenantContext"/> or throws if not set.
    /// Use in endpoint handlers where the tenant context middleware guarantees non-null.
    /// </summary>
    public static TenantContext GetRequiredTenant(this ITenantContextAccessor accessor) =>
        accessor.Current ?? throw new InvalidOperationException("Tenant context is not available. Ensure the request passed through TenantContextMiddleware.");
}
