using Microsoft.Extensions.Logging;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data.Entities;

namespace SmartKb.Data.Repositories;

public sealed class SqlAuditEventWriter : IAuditEventWriter
{
    private readonly SmartKbDbContext _db;
    private readonly ILogger<SqlAuditEventWriter> _logger;

    public SqlAuditEventWriter(SmartKbDbContext db, ILogger<SqlAuditEventWriter> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        var entity = new AuditEventEntity
        {
            Id = Guid.TryParse(auditEvent.EventId, out var id) ? id : Guid.NewGuid(),
            EventType = auditEvent.EventType,
            TenantId = auditEvent.TenantId,
            ActorId = auditEvent.ActorId,
            CorrelationId = auditEvent.CorrelationId,
            Timestamp = auditEvent.Timestamp,
            Detail = auditEvent.Detail,
        };

        _db.AuditEvents.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Audit event persisted: {EventType} by {ActorId} in tenant {TenantId}",
            auditEvent.EventType, auditEvent.ActorId, auditEvent.TenantId);
    }
}
