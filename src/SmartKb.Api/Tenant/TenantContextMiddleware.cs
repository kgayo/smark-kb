using System.Diagnostics;
using SmartKb.Contracts;
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

        var tenantId = context.User.FindFirst(EntraClaimTypes.TenantId)?.Value;
        var userId = context.User.FindFirst(EntraClaimTypes.ObjectId)?.Value
                     ?? context.User.FindFirst(EntraClaimTypes.Subject)?.Value;

        // Prefer inbound X-Correlation-Id header, fall back to W3C trace context, then TraceIdentifier.
        var inboundCorrelationId = context.Request.Headers[CustomHeaders.CorrelationId].FirstOrDefault();
        var correlationId = !string.IsNullOrEmpty(inboundCorrelationId)
            ? inboundCorrelationId
            : Activity.Current?.Id ?? context.TraceIdentifier;

        if (string.IsNullOrEmpty(tenantId))
        {
            _logger.LogWarning("Authenticated user {UserId} has no tenant claim (tid). Denying request.", userId);

            await auditWriter.WriteAsync(new AuditEvent(
                EventId: Guid.NewGuid().ToString(),
                EventType: AuditEventTypes.TenantMissing,
                TenantId: "",
                ActorId: userId ?? "unknown",
                CorrelationId: correlationId,
                Timestamp: DateTimeOffset.UtcNow,
                Detail: "Authenticated request denied: no tenant claim present."));

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        // Extract user group memberships from JWT claims for ACL security trimming (P0-014).
        // Entra ID sends groups in "groups" claim; roles in "roles" claim.
        var userGroups = context.User.Claims
            .Where(c => c.Type == EntraClaimTypes.Groups || c.Type == EntraClaimTypes.Roles)
            .Select(c => c.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var tenantContext = new TenantContext(tenantId, userId ?? "unknown", correlationId, userGroups);
        tenantAccessor.Current = tenantContext;

        // Attach tenant ID to Activity baggage for downstream correlation
        Activity.Current?.SetTag("tenant.id", tenantId);
        Activity.Current?.SetTag("user.id", userId);
        Activity.Current?.SetBaggage("tenantId", tenantId);

        // Echo correlation ID in response headers for client-side tracing.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[CustomHeaders.CorrelationId] = correlationId;
            return Task.CompletedTask;
        });

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
