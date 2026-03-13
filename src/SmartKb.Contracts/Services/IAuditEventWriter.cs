using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

public interface IAuditEventWriter
{
    Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default);
}
