using System.Diagnostics;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;

namespace SmartKb.Api.Tenant;

public sealed class TenantContextMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantContextMiddleware> _logger;

    public TenantContextMiddleware(RequestDelegate next, ILogger<TenantContextMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ITenantContextAccessor tenantAccessor, IAuditEventWriter auditWriter)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await _next(context);
            return;
        }

        var tenantId = context.User.FindFirst("tid")?.Value;
        var userId = context.User.FindFirst("oid")?.Value
                     ?? context.User.FindFirst("sub")?.Value;
        var correlationId = Activity.Current?.Id ?? context.TraceIdentifier;

        if (string.IsNullOrEmpty(tenantId))
        {
            _logger.LogWarning("Authenticated user {UserId} has no tenant claim (tid). Denying request.", userId);

            await auditWriter.WriteAsync(new AuditEvent(
                EventId: Guid.NewGuid().ToString(),
                EventType: "tenant.missing",
                TenantId: "",
                ActorId: userId ?? "unknown",
                CorrelationId: correlationId,
                Timestamp: DateTimeOffset.UtcNow,
                Detail: "Authenticated request denied: no tenant claim present."));

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        var tenantContext = new TenantContext(tenantId, userId ?? "unknown", correlationId);
        tenantAccessor.Current = tenantContext;

        // Attach tenant ID to Activity baggage for downstream correlation
        Activity.Current?.SetTag("tenant.id", tenantId);
        Activity.Current?.SetTag("user.id", userId);
        Activity.Current?.SetBaggage("tenantId", tenantId);

        using (_logger.BeginScope(new Dictionary<string, object?>
        {
            ["TenantId"] = tenantId,
            ["UserId"] = userId,
            ["CorrelationId"] = correlationId,
        }))
        {
            await _next(context);
        }
    }
}
