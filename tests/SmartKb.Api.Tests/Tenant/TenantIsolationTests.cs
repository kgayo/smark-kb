using System.Net;
using System.Net.Http.Json;
using SmartKb.Api.Audit;
using SmartKb.Api.Tests.Auth;
using SmartKb.Contracts.Models;
using Microsoft.Extensions.DependencyInjection;

namespace SmartKb.Api.Tests.Tenant;

public class TenantIsolationTests : IClassFixture<AuthTestFactory>
{
    private readonly AuthTestFactory _factory;

    public TenantIsolationTests(AuthTestFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient() => _factory.CreateClient();

    private static void AddAuth(
        HttpRequestMessage request,
        string? roles = null,
        string? tenantId = null,
        string? userId = null)
    {
        request.Headers.Add(TestAuthHandler.AuthenticatedHeader, "true");
        if (roles is not null)
            request.Headers.Add(TestAuthHandler.RolesHeader, roles);
        if (tenantId is not null)
            request.Headers.Add(TestAuthHandler.TenantHeader, tenantId);
        if (userId is not null)
            request.Headers.Add(TestAuthHandler.UserIdHeader, userId);
    }

    // --- Tenant context propagation ---

    [Fact]
    public async Task ApiMe_ReturnsTenantContext_WithCorrelationId()
    {
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        AddAuth(request, roles: "SupportAgent", tenantId: "tenant-abc", userId: "user-123");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<MeWithCorrelationResponse>();
        Assert.NotNull(body);
        Assert.Equal("tenant-abc", body.TenantId);
        Assert.Equal("user-123", body.UserId);
        Assert.NotNull(body.CorrelationId);
        Assert.NotEmpty(body.CorrelationId!);
    }

    // --- Tenant-scoped endpoints return tenant ID ---

    [Fact]
    public async Task AdminConnectors_ReturnsTenantId()
    {
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/connectors");
        AddAuth(request, roles: "Admin", tenantId: "tenant-xyz");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<TenantScopedResponse>();
        Assert.NotNull(body);
        Assert.Equal("tenant-xyz", body.TenantId);
    }

    [Fact]
    public async Task AuditEvents_ReturnsTenantId()
    {
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/audit/events");
        AddAuth(request, roles: "Admin", tenantId: "tenant-xyz");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<TenantScopedResponse>();
        Assert.NotNull(body);
        Assert.Equal("tenant-xyz", body.TenantId);
    }

    // --- Cross-tenant access denied ---

    [Fact]
    public async Task CrossTenantConnectorAccess_Returns403()
    {
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/connectors/other-tenant");
        AddAuth(request, roles: "Admin", tenantId: "my-tenant", userId: "user-1");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SameTenantConnectorAccess_Returns200()
    {
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/connectors/my-tenant");
        AddAuth(request, roles: "Admin", tenantId: "my-tenant", userId: "user-1");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CrossTenantAccess_GeneratesAuditEvent()
    {
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/connectors/foreign-tenant");
        AddAuth(request, roles: "Admin", tenantId: "home-tenant", userId: "attacker-user");

        await client.SendAsync(request);

        var auditWriter = _factory.Services.GetRequiredService<InMemoryAuditEventWriter>();
        var events = auditWriter.GetEvents();
        var crossTenantEvent = events.FirstOrDefault(e =>
            e.EventType == "tenant.cross_access_denied" &&
            e.ActorId == "attacker-user" &&
            e.TenantId == "home-tenant");
        Assert.NotNull(crossTenantEvent);
        Assert.Contains("foreign-tenant", crossTenantEvent!.Detail);
    }

    [Fact]
    public async Task CrossTenantAccess_CaseInsensitiveMatch_Returns200()
    {
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/connectors/MY-TENANT");
        AddAuth(request, roles: "Admin", tenantId: "my-tenant", userId: "user-1");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // --- No tenant claim produces 403 on protected endpoints ---

    [Fact]
    public async Task ProtectedEndpoint_Returns403_WhenNoTenantClaim()
    {
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        // Authenticate without tenant
        request.Headers.Add(TestAuthHandler.AuthenticatedHeader, "true");
        request.Headers.Add(TestAuthHandler.RolesHeader, "Admin");
        request.Headers.Add(TestAuthHandler.TenantHeader, "");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- Anonymous endpoints still work ---

    [Theory]
    [InlineData("/healthz")]
    [InlineData("/api/health")]
    [InlineData("/")]
    public async Task AnonymousEndpoints_BypassTenantMiddleware(string path)
    {
        var client = CreateClient();
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed record MeWithCorrelationResponse(
        string? UserId, string? Name, string? TenantId, string? CorrelationId, string[] Roles);

    private sealed record TenantScopedResponse(string? TenantId, string? Message);
}
