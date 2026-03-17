using System.Net;
using System.Text.Json;
using SmartKb.Api.Tests.Auth;

namespace SmartKb.Api.Tests.Secrets;

public class SecretStatusEndpointTests : IClassFixture<AuthTestFactory>
{
    private readonly HttpClient _client;

    public SecretStatusEndpointTests(AuthTestFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task SecretStatus_WithAdminRole_ReturnsStatus()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/secrets/status");
        request.Headers.Add(TestAuthHandler.AuthenticatedHeader, "true");
        request.Headers.Add(TestAuthHandler.RolesHeader, "Admin");
        request.Headers.Add(TestAuthHandler.TenantHeader, "tenant-1");
        request.Headers.Add(TestAuthHandler.UserIdHeader, "admin-user");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal("tenant-1", root.GetProperty("tenantId").GetString());
        Assert.True(root.GetProperty("keyVaultConfigured").GetBoolean());
        Assert.False(root.GetProperty("openAiKeyConfigured").GetBoolean());
        Assert.Equal("gpt-4o", root.GetProperty("openAiModel").GetString());
    }

    [Fact]
    public async Task SecretStatus_WithoutAdminRole_Returns403()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/secrets/status");
        request.Headers.Add(TestAuthHandler.AuthenticatedHeader, "true");
        request.Headers.Add(TestAuthHandler.RolesHeader, "SupportAgent");
        request.Headers.Add(TestAuthHandler.TenantHeader, "tenant-1");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SecretStatus_Unauthenticated_Returns401()
    {
        var response = await _client.GetAsync("/api/admin/secrets/status");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SecretStatus_DoesNotExposeRawSecrets()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/secrets/status");
        request.Headers.Add(TestAuthHandler.AuthenticatedHeader, "true");
        request.Headers.Add(TestAuthHandler.RolesHeader, "Admin");
        request.Headers.Add(TestAuthHandler.TenantHeader, "tenant-1");

        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("apikey", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sk-", body);
        Assert.DoesNotContain("secret", body, StringComparison.OrdinalIgnoreCase);
    }
}
