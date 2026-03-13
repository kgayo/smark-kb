namespace SmartKb.Contracts.Models;

public sealed record TenantContext(
    string TenantId,
    string UserId,
    string CorrelationId);
