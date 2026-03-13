using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;
using SmartKb.Api.Auth;
using SmartKb.Contracts.Models;

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

var app = builder.Build();

var version = Assembly.GetExecutingAssembly()
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.1.0";

app.UseAuthentication();
app.UseAuthorization();

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

app.MapGet("/api/me", (HttpContext ctx) =>
{
    var roles = PermissionAuthorizationHandler.GetAppRoles(ctx.User).ToList();
    return Results.Ok(new
    {
        userId = ctx.User.FindFirst("oid")?.Value ?? ctx.User.FindFirst("sub")?.Value,
        name = ctx.User.FindFirst("name")?.Value,
        tenantId = ctx.User.FindFirst("tid")?.Value,
        roles = roles.Select(r => r.ToString()),
    });
});

app.MapGet("/api/admin/connectors", () =>
    Results.Ok(new { message = "Connector list placeholder" }))
    .RequirePermission("connector:manage");

app.MapGet("/api/audit/events", () =>
    Results.Ok(new { message = "Audit events placeholder" }))
    .RequirePermission("audit:read");

app.Run();

public partial class Program { }
