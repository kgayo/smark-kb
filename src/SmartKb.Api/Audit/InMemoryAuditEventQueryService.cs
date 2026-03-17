using System.Runtime.CompilerServices;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;

namespace SmartKb.Api.Audit;

public sealed class InMemoryAuditEventQueryService : IAuditEventQueryService
{
    private readonly InMemoryAuditEventWriter _writer;

    public InMemoryAuditEventQueryService(InMemoryAuditEventWriter writer)
    {
        _writer = writer;
    }

    public Task<AuditEventListResponse> QueryAsync(
        string tenantId,
        AuditEventQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        var all = _writer.GetEvents()
            .Where(e => e.TenantId == tenantId);

        all = ApplyFilters(all, request.EventType, request.ActorId,
            request.From, request.To, request.CorrelationId);

        var ordered = all.OrderByDescending(e => e.Timestamp).ToList();
        var pageSize = Math.Clamp(request.PageSize, 1, 200);
        var page = Math.Max(request.Page, 1);
        var totalCount = ordered.Count;

        var events = ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ToResponse)
            .ToList();

        return Task.FromResult(new AuditEventListResponse
        {
            Events = events,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            HasMore = page * pageSize < totalCount,
        });
    }

    public async IAsyncEnumerable<AuditEventResponse> ExportAsync(
        string tenantId,
        AuditExportCursor cursor,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var all = _writer.GetEvents()
            .Where(e => e.TenantId == tenantId);

        all = ApplyFilters(all, cursor.EventType, cursor.ActorId,
            cursor.From, cursor.To, correlationId: null);

        var ordered = all.OrderByDescending(e => e.Timestamp).ToList();

        if (cursor.AfterTimestamp.HasValue)
        {
            ordered = ordered
                .Where(e => e.Timestamp < cursor.AfterTimestamp.Value)
                .ToList();
        }

        var limit = Math.Clamp(cursor.Limit, 1, 5000);

        foreach (var e in ordered.Take(limit))
        {
            yield return ToResponse(e);
        }

        await Task.CompletedTask;
    }

    private static IEnumerable<AuditEvent> ApplyFilters(
        IEnumerable<AuditEvent> events,
        string? eventType,
        string? actorId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? correlationId)
    {
        if (!string.IsNullOrWhiteSpace(eventType))
            events = events.Where(e => e.EventType == eventType);

        if (!string.IsNullOrWhiteSpace(actorId))
            events = events.Where(e => e.ActorId == actorId);

        if (from.HasValue)
            events = events.Where(e => e.Timestamp >= from.Value);

        if (to.HasValue)
            events = events.Where(e => e.Timestamp <= to.Value);

        if (!string.IsNullOrWhiteSpace(correlationId))
            events = events.Where(e => e.CorrelationId == correlationId);

        return events;
    }

    private static AuditEventResponse ToResponse(AuditEvent e) => new()
    {
        EventId = e.EventId,
        EventType = e.EventType,
        TenantId = e.TenantId,
        ActorId = e.ActorId,
        CorrelationId = e.CorrelationId,
        Timestamp = e.Timestamp,
        Detail = e.Detail,
    };
}
