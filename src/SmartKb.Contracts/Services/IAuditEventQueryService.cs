using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

public interface IAuditEventQueryService
{
    Task<AuditEventListResponse> QueryAsync(
        string tenantId,
        AuditEventQueryRequest request,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<AuditEventResponse> ExportAsync(
        string tenantId,
        AuditExportCursor cursor,
        CancellationToken cancellationToken = default);
}
