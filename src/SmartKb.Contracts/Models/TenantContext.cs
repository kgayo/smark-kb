namespace SmartKb.Contracts.Models;

public sealed record TenantContext(
    string TenantId,
    string UserId,
    string CorrelationId,
    IReadOnlyList<string> UserGroups)
{
    /// <summary>Backward-compatible constructor without UserGroups.</summary>
    public TenantContext(string tenantId, string userId, string correlationId)
        : this(tenantId, userId, correlationId, []) { }
}
