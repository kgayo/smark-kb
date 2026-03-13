using System.Net;
using System.Net.Http.Json;

namespace SmartKb.Api.Tests.Auth;

public class AuthorizationTests : IClassFixture<AuthTestFactory>
{
    private readonly AuthTestFactory _factory;

    public AuthorizationTests(AuthTestFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient() => _factory.CreateClient();

    private static void AddAuth(HttpRequestMessage request, string? roles = null, string? tenantId = null, string? userId = null)
    {
        request.Headers.Add(TestAuthHandler.AuthenticatedHeader, "true");
        if (roles is not null)
            request.Headers.Add(TestAuthHandler.RolesHeader, roles);
        if (tenantId is not null)
            request.Headers.Add(TestAuthHandler.TenantHeader, tenantId);
        if (userId is not null)
            request.Headers.Add(TestAuthHandler.UserIdHeader, userId);
    }

    // --- Anonymous endpoints remain accessible ---

    [Theory]
    [InlineData("/healthz")]
    [InlineData("/api/health")]
    [InlineData("/")]
    public async Task AnonymousEndpoints_Return200_WithoutAuth(string path)
    {
        var client = CreateClient();
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // --- Protected endpoints return 401 without auth ---

    [Theory]
    [InlineData("/api/me")]
    [InlineData("/api/admin/connectors")]
    [InlineData("/api/audit/events")]
    public async Task ProtectedEndpoints_Return401_WithoutAuth(string path)
    {
        var client = CreateClient();
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- /api/me returns 200 for any authenticated user ---

    [Fact]
    public async Task ApiMe_ReturnsUserInfo_WhenAuthenticated()
    {
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        AddAuth(request, roles: "SupportAgent", tenantId: "tenant-abc", userId: "user-123");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<MeResponse>();
        Assert.NotNull(body);
        Assert.Equal("user-123", body.UserId);
        Assert.Equal("tenant-abc", body.TenantId);
        Assert.Contains("SupportAgent", body.Roles);
    }

    // --- Permission-gated endpoints return 403 for wrong role ---

    [Fact]
    public async Task AdminConnectors_Returns403_ForSupportAgent()
    {
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/connectors");
        AddAuth(request, roles: "SupportAgent");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminConnectors_Returns200_ForAdmin()
    {
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/connectors");
        AddAuth(request, roles: "Admin");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AuditEvents_Returns403_ForSupportAgent()
    {
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/audit/events");
        AddAuth(request, roles: "SupportAgent");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AuditEvents_Returns200_ForSecurityAuditor()
    {
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/audit/events");
        AddAuth(request, roles: "SecurityAuditor");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AuditEvents_Returns200_ForAdmin()
    {
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/audit/events");
        AddAuth(request, roles: "Admin");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // --- EngineeringViewer cannot access admin or audit ---

    [Theory]
    [InlineData("/api/admin/connectors")]
    [InlineData("/api/audit/events")]
    public async Task EngineeringViewer_Returns403_ForRestrictedEndpoints(string path)
    {
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        AddAuth(request, roles: "EngineeringViewer");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- Authenticated user with no roles gets 403 on permission-gated endpoints ---

    [Theory]
    [InlineData("/api/admin/connectors")]
    [InlineData("/api/audit/events")]
    public async Task AuthenticatedNoRole_Returns403_OnPermissionGated(string path)
    {
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        AddAuth(request); // authenticated but no roles

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- /api/me with no roles returns empty roles array ---

    [Fact]
    public async Task ApiMe_ReturnsEmptyRoles_WhenNoRoleClaims()
    {
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        AddAuth(request);

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<MeResponse>();
        Assert.NotNull(body);
        Assert.Empty(body.Roles);
    }

    private sealed record MeResponse(string? UserId, string? Name, string? TenantId, string[] Roles);
}
