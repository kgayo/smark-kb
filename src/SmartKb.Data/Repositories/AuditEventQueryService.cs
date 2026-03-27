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
        var pageSize = PaginationDefaults.ClampAuditPageSize(request.PageSize);
        var page = Math.Max(request.Page, 1);

        var query = _db.AuditEvents.AsNoTracking()
            .Where(e => e.TenantId == tenantId);

        query = ApplyStringFilters(query, request.EventType, request.ActorId, request.CorrelationId);
        query = ApplyEpochDateFilters(query, request.From, request.To);

        var totalCount = await query.CountAsync(cancellationToken);

        var entities = await query
            .OrderByDescending(e => e.TimestampEpoch)
            .ThenByDescending(e => e.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var events = entities.Select(MapToResponse).ToList();

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
        query = ApplyEpochDateFilters(query, cursor.From, cursor.To);

        // Cursor-based pagination: events after the cursor position
        if (cursor.AfterTimestamp.HasValue && cursor.AfterId.HasValue)
        {
            var afterEpoch = cursor.AfterTimestamp.Value.ToUnixTimeSeconds();
            var afterId = cursor.AfterId.Value;
            query = query.Where(e =>
                e.TimestampEpoch < afterEpoch ||
                (e.TimestampEpoch == afterEpoch && e.Id.CompareTo(afterId) < 0));
        }
        else if (cursor.AfterTimestamp.HasValue)
        {
            var afterEpoch = cursor.AfterTimestamp.Value.ToUnixTimeSeconds();
            query = query.Where(e => e.TimestampEpoch < afterEpoch);
        }

        var entities = await query
            .OrderByDescending(e => e.TimestampEpoch)
            .ThenByDescending(e => e.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

        var results = entities.Select(MapToResponse).ToList();

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

    private static IQueryable<AuditEventEntity> ApplyEpochDateFilters(
        IQueryable<AuditEventEntity> query,
        DateTimeOffset? from,
        DateTimeOffset? to)
    {
        if (from.HasValue)
        {
            var fromEpoch = from.Value.ToUnixTimeSeconds();
            query = query.Where(e => e.TimestampEpoch >= fromEpoch);
        }

        if (to.HasValue)
        {
            var toEpoch = to.Value.ToUnixTimeSeconds();
            query = query.Where(e => e.TimestampEpoch <= toEpoch);
        }

        return query;
    }
}
