using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data.Entities;

namespace SmartKb.Data.Repositories;

public sealed class AuditEventQueryService : IAuditEventQueryService
{
    private readonly SmartKbDbContext _db;
    private readonly ILogger<AuditEventQueryService> _logger;

    private const int MaxPageSize = 200;
    private const int MaxExportBatchSize = 5000;

    public AuditEventQueryService(SmartKbDbContext db, ILogger<AuditEventQueryService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<AuditEventListResponse> QueryAsync(
        string tenantId,
        AuditEventQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        var pageSize = Math.Clamp(request.PageSize, 1, MaxPageSize);
        var page = Math.Max(request.Page, 1);

        var query = _db.AuditEvents.AsNoTracking()
            .Where(e => e.TenantId == tenantId);

        query = ApplyStringFilters(query, request.EventType, request.ActorId, request.CorrelationId);

        var entities = await query.ToListAsync(cancellationToken);

        // Date range and ordering applied client-side (DateTimeOffset not sortable/comparable in SQLite)
        IEnumerable<AuditEventEntity> filtered = entities;
        filtered = ApplyDateFilters(filtered, request.From, request.To);

        var sorted = filtered
            .OrderByDescending(e => e.Timestamp)
            .ThenByDescending(e => e.Id)
            .ToList();

        var totalCount = sorted.Count;

        var events = sorted
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(MapToResponse)
            .ToList();

        _logger.LogInformation(
            "Audit query: tenant={TenantId}, eventType={EventType}, page={Page}, returned={Count}/{Total}",
            tenantId, request.EventType ?? "(all)", page, events.Count, totalCount);

        return new AuditEventListResponse
        {
            Events = events,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            HasMore = page * pageSize < totalCount,
        };
    }

    public async IAsyncEnumerable<AuditEventResponse> ExportAsync(
        string tenantId,
        AuditExportCursor cursor,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var limit = Math.Clamp(cursor.Limit, 1, MaxExportBatchSize);

        var query = _db.AuditEvents.AsNoTracking()
            .Where(e => e.TenantId == tenantId);

        query = ApplyStringFilters(query, cursor.EventType, cursor.ActorId, correlationId: null);

        var entities = await query.ToListAsync(cancellationToken);

        IEnumerable<AuditEventEntity> filtered = ApplyDateFilters(entities, cursor.From, cursor.To);

        var sorted = filtered
            .OrderByDescending(e => e.Timestamp)
            .ThenByDescending(e => e.Id);

        // Cursor-based pagination: events after the cursor position
        IEnumerable<AuditEventEntity> cursored = sorted;
        if (cursor.AfterTimestamp.HasValue && cursor.AfterId.HasValue)
        {
            var afterTs = cursor.AfterTimestamp.Value;
            var afterId = cursor.AfterId.Value;
            cursored = sorted.Where(e =>
                e.Timestamp < afterTs ||
                (e.Timestamp == afterTs && e.Id.CompareTo(afterId) < 0));
        }
        else if (cursor.AfterTimestamp.HasValue)
        {
            cursored = sorted.Where(e => e.Timestamp < cursor.AfterTimestamp.Value);
        }

        var results = cursored.Take(limit).Select(MapToResponse).ToList();

        _logger.LogInformation(
            "Audit export: tenant={TenantId}, cursor={AfterTimestamp}, returned={Count}",
            tenantId, cursor.AfterTimestamp?.ToString("O") ?? "(start)", results.Count);

        foreach (var e in results)
        {
            yield return e;
        }
    }

    private static AuditEventResponse MapToResponse(AuditEventEntity e) => new()
    {
        EventId = e.Id.ToString(),
        EventType = e.EventType,
        TenantId = e.TenantId,
        ActorId = e.ActorId,
        CorrelationId = e.CorrelationId,
        Timestamp = e.Timestamp,
        Detail = e.Detail,
    };

    private static IQueryable<AuditEventEntity> ApplyStringFilters(
        IQueryable<AuditEventEntity> query,
        string? eventType,
        string? actorId,
        string? correlationId)
    {
        if (!string.IsNullOrWhiteSpace(eventType))
            query = query.Where(e => e.EventType == eventType);

        if (!string.IsNullOrWhiteSpace(actorId))
            query = query.Where(e => e.ActorId == actorId);

        if (!string.IsNullOrWhiteSpace(correlationId))
            query = query.Where(e => e.CorrelationId == correlationId);

        return query;
    }

    private static IEnumerable<AuditEventEntity> ApplyDateFilters(
        IEnumerable<AuditEventEntity> events,
        DateTimeOffset? from,
        DateTimeOffset? to)
    {
        if (from.HasValue)
            events = events.Where(e => e.Timestamp >= from.Value);

        if (to.HasValue)
            events = events.Where(e => e.Timestamp <= to.Value);

        return events;
    }
}
