namespace SmartKb.Contracts.Models;

public sealed record AuditEvent(
    string EventId,
    string EventType,
    string TenantId,
    string ActorId,
    string CorrelationId,
    DateTimeOffset Timestamp,
    string Detail);
