using System.Collections.Concurrent;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;

namespace SmartKb.Api.Audit;

public sealed class InMemoryAuditEventWriter : IAuditEventWriter
{
    private readonly ConcurrentBag<AuditEvent> _events = new();
    private readonly ILogger<InMemoryAuditEventWriter> _logger;

    public InMemoryAuditEventWriter(ILogger<InMemoryAuditEventWriter> logger)
    {
        _logger = logger;
    }

    public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        _events.Add(auditEvent);
        _logger.LogInformation(
            "Audit event {EventType} for tenant {TenantId} by actor {ActorId}: {Detail}",
            auditEvent.EventType, auditEvent.TenantId, auditEvent.ActorId, auditEvent.Detail);
        return Task.CompletedTask;
    }

    public IReadOnlyList<AuditEvent> GetEvents() => _events.ToList().AsReadOnly();
}
