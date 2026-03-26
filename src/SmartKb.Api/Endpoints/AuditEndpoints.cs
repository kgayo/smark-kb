using Microsoft.AspNetCore.Mvc;
using SmartKb.Api.Auth;
using SmartKb.Contracts;
using SmartKb.Api.Tenant;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data.Repositories;

namespace SmartKb.Api.Endpoints;

public static class AuditEndpoints
{
    public static WebApplication MapAuditEndpoints(this WebApplication app)
    {
        app.MapGet("/api/audit/events", async (
            HttpContext httpContext,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] IAuditEventQueryService auditQuery,
            [FromQuery] string? eventType,
            [FromQuery] string? actorId,
            [FromQuery] string? correlationId,
            [FromQuery] DateTimeOffset? from,
            [FromQuery] DateTimeOffset? to,
            [FromQuery] int? page,
            [FromQuery] int? pageSize) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var request = new AuditEventQueryRequest
            {
                EventType = eventType,
                ActorId = actorId,
                CorrelationId = correlationId,
                From = from,
                To = to,
                Page = page ?? 1,
                PageSize = pageSize ?? 50,
            };
            var result = await auditQuery.QueryAsync(tenant.TenantId, request, ct);
            return Results.Ok(ApiResponse<AuditEventListResponse>.Success(result, tenant.CorrelationId));
        }).RequirePermission(Permissions.AuditRead);

        app.MapGet("/api/audit/events/export", async (
            HttpContext httpContext,
            [FromServices] ITenantContextAccessor tenantAccessor,
            [FromServices] IAuditEventQueryService auditQuery,
            [FromQuery] string? eventType,
            [FromQuery] string? actorId,
            [FromQuery] DateTimeOffset? from,
            [FromQuery] DateTimeOffset? to,
            [FromQuery] DateTimeOffset? afterTimestamp,
            [FromQuery] Guid? afterId,
            [FromQuery] int? limit) =>
        {
            var tenant = tenantAccessor.GetRequiredTenant();
            var ct = httpContext.RequestAborted;
            var cursor = new AuditExportCursor
            {
                EventType = eventType,
                ActorId = actorId,
                From = from,
                To = to,
                AfterTimestamp = afterTimestamp,
                AfterId = afterId,
                Limit = limit ?? 1000,
            };

            httpContext.Response.ContentType = "application/x-ndjson";
            httpContext.Response.Headers["X-Correlation-Id"] = tenant.CorrelationId;

            AuditEventResponse? lastEvent = null;
            var count = 0;

            await foreach (var auditEvent in auditQuery.ExportAsync(tenant.TenantId, cursor, ct))
            {
                var json = System.Text.Json.JsonSerializer.Serialize(auditEvent);
                await httpContext.Response.WriteAsync(json + "\n", ct);
                lastEvent = auditEvent;
                count++;
            }

            // Write cursor metadata as last NDJSON line if there are results
            if (lastEvent is not null)
            {
                var nextCursor = new
                {
                    __cursor = true,
                    afterTimestamp = lastEvent.Timestamp,
                    afterId = lastEvent.EventId,
                    hasMore = count >= cursor.Limit,
                };
                var cursorJson = System.Text.Json.JsonSerializer.Serialize(nextCursor);
                await httpContext.Response.WriteAsync(cursorJson + "\n", ct);
            }
        }).RequirePermission(Permissions.AuditExport);

        return app;
    }
}
