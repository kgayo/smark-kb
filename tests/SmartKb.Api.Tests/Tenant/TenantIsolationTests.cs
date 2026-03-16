using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SmartKb.Api.Audit;
using SmartKb.Api.Tests.Auth;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Data;
using SmartKb.Data.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

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

    private async Task<Guid> SeedConnectorAsync(string tenantId, string name = "Test Connector")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmartKbDbContext>();

        // Ensure tenant exists
        if (!await db.Tenants.AnyAsync(t => t.TenantId == tenantId))
        {
            db.Tenants.Add(new TenantEntity
            {
                TenantId = tenantId,
                DisplayName = $"Tenant {tenantId}",
                CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var connector = new ConnectorEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            ConnectorType = ConnectorType.AzureDevOps,
            Status = ConnectorStatus.Enabled,
            AuthType = SecretAuthType.Pat,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.Connectors.Add(connector);
        await db.SaveChangesAsync();
        return connector.Id;
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

    // --- Tenant-scoped endpoints return data ---

    [Fact]
    public async Task AdminConnectors_ReturnsSuccessForAuthenticatedTenant()
    {
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/connectors");
        AddAuth(request, roles: "Admin", tenantId: "tenant-1");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("data", out _), "Response should have a 'data' property");
        Assert.True(json.TryGetProperty("correlationId", out _), "Response should have a 'correlationId' property");
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

    // --- Cross-tenant access denied (returns 404 — no information leakage) ---

    [Fact]
    public async Task CrossTenantConnectorAccess_Returns404()
    {
        // Create connector in tenant-1
        var connectorId = await SeedConnectorAsync("tenant-1", "Cross-Tenant Test Connector");

        // Access from tenant-other JWT — should not find the connector
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/admin/connectors/{connectorId}");
        AddAuth(request, roles: "Admin", tenantId: "tenant-other", userId: "other-user");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SameTenantConnectorAccess_Returns200()
    {
        // Create connector in tenant-1
        var connectorId = await SeedConnectorAsync("tenant-1", "Same-Tenant Test Connector");

        // Access from tenant-1 JWT — should find the connector
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/admin/connectors/{connectorId}");
        AddAuth(request, roles: "Admin", tenantId: "tenant-1", userId: "user-1");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task MissingTenantClaim_GeneratesAuditEvent()
    {
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/connectors");
        // Authenticate without tenant — triggers tenant.missing audit event
        request.Headers.Add(TestAuthHandler.AuthenticatedHeader, "true");
        request.Headers.Add(TestAuthHandler.RolesHeader, "Admin");
        request.Headers.Add(TestAuthHandler.TenantHeader, "");

        await client.SendAsync(request);

        var auditWriter = _factory.Services.GetRequiredService<InMemoryAuditEventWriter>();
        var events = auditWriter.GetEvents();
        var missingTenantEvent = events.FirstOrDefault(e =>
            e.EventType == "tenant.missing");
        Assert.NotNull(missingTenantEvent);
        Assert.Contains("no tenant claim", missingTenantEvent!.Detail);
    }

    [Fact]
    public async Task CrossTenantConnectorAccess_DoesNotLeakExistence()
    {
        // Create connector in tenant-1
        var connectorId = await SeedConnectorAsync("tenant-1", "Leak-Test Connector");

        // Access same connector from tenant-other — should get same 404 as a non-existent connector
        var client = CreateClient();

        var crossTenantRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/admin/connectors/{connectorId}");
        AddAuth(crossTenantRequest, roles: "Admin", tenantId: "tenant-other", userId: "attacker");
        var crossTenantResponse = await client.SendAsync(crossTenantRequest);

        var nonExistentRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/admin/connectors/{Guid.NewGuid()}");
        AddAuth(nonExistentRequest, roles: "Admin", tenantId: "tenant-1", userId: "user-1");
        var nonExistentResponse = await client.SendAsync(nonExistentRequest);

        // Both should return 404 — attacker cannot distinguish cross-tenant from non-existent
        Assert.Equal(HttpStatusCode.NotFound, crossTenantResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, nonExistentResponse.StatusCode);
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
