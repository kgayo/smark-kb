using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;
using SmartKb.Api.Audit;
using SmartKb.Api.Auth;
using SmartKb.Api.Tenant;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();

if (!string.IsNullOrEmpty(builder.Configuration["AzureAd:ClientId"])
    && !builder.Configuration["AzureAd:ClientId"]!.StartsWith('<'))
{
    builder.Services.AddMicrosoftIdentityWebApiAuthentication(builder.Configuration);
}
else
{
    builder.Services.AddAuthentication();
}

builder.Services.AddAuthorizationBuilder()
    .AddPermissionPolicies()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());

builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddScoped<ITenantContextAccessor, TenantContextAccessor>();
builder.Services.AddSingleton<InMemoryAuditEventWriter>();
builder.Services.AddSingleton<IAuditEventWriter>(sp => sp.GetRequiredService<InMemoryAuditEventWriter>());

var app = builder.Build();

var version = Assembly.GetExecutingAssembly()
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.1.0";

app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<TenantContextMiddleware>();

app.MapHealthChecks("/healthz").AllowAnonymous();

app.MapGet("/api/health", () =>
{
    var status = new HealthStatus(
        Service: "SmartKb.Api",
        Status: "Healthy",
        Version: version,
        Timestamp: DateTimeOffset.UtcNow);
    return Results.Ok(status);
}).AllowAnonymous();

app.MapGet("/", () => Results.Ok(new { service = "SmartKb.Api", status = "running" }))
    .AllowAnonymous();

app.MapGet("/api/me", (ITenantContextAccessor tenantAccessor, HttpContext ctx) =>
{
    var roles = PermissionAuthorizationHandler.GetAppRoles(ctx.User).ToList();
    var tenant = tenantAccessor.Current;
    return Results.Ok(new
    {
        userId = tenant?.UserId ?? ctx.User.FindFirst("oid")?.Value ?? ctx.User.FindFirst("sub")?.Value,
        name = ctx.User.FindFirst("name")?.Value,
        tenantId = tenant?.TenantId ?? ctx.User.FindFirst("tid")?.Value,
        correlationId = tenant?.CorrelationId,
        roles = roles.Select(r => r.ToString()),
    });
});

app.MapGet("/api/admin/connectors", (ITenantContextAccessor tenantAccessor) =>
{
    var tenant = tenantAccessor.Current!;
    return Results.Ok(new { tenantId = tenant.TenantId, message = "Connector list placeholder" });
}).RequirePermission("connector:manage");

app.MapGet("/api/admin/connectors/{connectorTenantId}", async (
    string connectorTenantId,
    ITenantContextAccessor tenantAccessor,
    IAuditEventWriter auditWriter) =>
{
    var tenant = tenantAccessor.Current!;

    if (!string.Equals(tenant.TenantId, connectorTenantId, StringComparison.OrdinalIgnoreCase))
    {
        await auditWriter.WriteAsync(new AuditEvent(
            EventId: Guid.NewGuid().ToString(),
            EventType: "tenant.cross_access_denied",
            TenantId: tenant.TenantId,
            ActorId: tenant.UserId,
            CorrelationId: tenant.CorrelationId,
            Timestamp: DateTimeOffset.UtcNow,
            Detail: $"Cross-tenant access denied: user from tenant '{tenant.TenantId}' attempted to access connector resources for tenant '{connectorTenantId}'."));

        return Results.Forbid();
    }

    return Results.Ok(new { tenantId = tenant.TenantId, message = "Connector detail placeholder" });
}).RequirePermission("connector:manage");

app.MapGet("/api/audit/events", (ITenantContextAccessor tenantAccessor) =>
{
    var tenant = tenantAccessor.Current!;
    return Results.Ok(new { tenantId = tenant.TenantId, message = "Audit events placeholder" });
}).RequirePermission("audit:read");

app.Run();

public partial class Program { }
