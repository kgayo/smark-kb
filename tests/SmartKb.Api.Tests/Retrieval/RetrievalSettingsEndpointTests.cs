using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using SmartKb.Api.Tests.Auth;

namespace SmartKb.Api.Tests.Retrieval;

public sealed class RetrievalSettingsEndpointTests : IClassFixture<AuthTestFactory>
{
    private readonly AuthTestFactory _factory;

    public RetrievalSettingsEndpointTests(AuthTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetSettings_ReturnsDefaults_ForAdmin()
    {
        var client = CreateAdminClient("tenant-ret");

        var response = await client.GetAsync("/api/admin/retrieval-settings");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        var data = json.RootElement.GetProperty("data");

        Assert.Equal("tenant-ret", data.GetProperty("tenantId").GetString());
        Assert.Equal(20, data.GetProperty("topK").GetInt32());
        Assert.True(data.GetProperty("enableSemanticReranking").GetBoolean());
        Assert.False(data.GetProperty("hasOverrides").GetBoolean());
    }

    [Fact]
    public async Task UpdateSettings_CreatesOverride()
    {
        var client = CreateAdminClient("tenant-ret-upd");

        var request = new { topK = 30, enablePatternFusion = false };
        var response = await client.PutAsJsonAsync("/api/admin/retrieval-settings", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        var data = json.RootElement.GetProperty("data");

        Assert.Equal(30, data.GetProperty("topK").GetInt32());
        Assert.False(data.GetProperty("enablePatternFusion").GetBoolean());
        Assert.True(data.GetProperty("hasOverrides").GetBoolean());
    }

    [Fact]
    public async Task DeleteSettings_ResetToDefaults()
    {
        var client = CreateAdminClient("tenant-ret-del");

        // First create an override.
        await client.PutAsJsonAsync("/api/admin/retrieval-settings", new { topK = 50 });

        // Then delete it.
        var response = await client.DeleteAsync("/api/admin/retrieval-settings");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify reset.
        var getResponse = await client.GetAsync("/api/admin/retrieval-settings");
        var body = await getResponse.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        var data = json.RootElement.GetProperty("data");

        Assert.False(data.GetProperty("hasOverrides").GetBoolean());
        Assert.Equal(20, data.GetProperty("topK").GetInt32());
    }

    [Fact]
    public async Task DeleteSettings_NotFound_WhenNoOverrides()
    {
        var client = CreateAdminClient("tenant-ret-nf");

        var response = await client.DeleteAsync("/api/admin/retrieval-settings");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetSettings_RequiresConnectorManagePermission()
    {
        var client = CreateClient("SupportAgent", "tenant-ret-rbac");

        var response = await client.GetAsync("/api/admin/retrieval-settings");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateSettings_RequiresConnectorManagePermission()
    {
        var client = CreateClient("SupportAgent", "tenant-ret-rbac2");

        var response = await client.PutAsJsonAsync("/api/admin/retrieval-settings", new { topK = 30 });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetSettings_RequiresAuthentication()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await client.GetAsync("/api/admin/retrieval-settings");
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    private HttpClient CreateAdminClient(string tenantId)
    {
        return CreateClient("Admin", tenantId);
    }

    private HttpClient CreateClient(string role, string tenantId)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        client.DefaultRequestHeaders.Add("X-Test-Auth", "true");
        client.DefaultRequestHeaders.Add("X-Test-Roles", role);
        client.DefaultRequestHeaders.Add("X-Test-Tenant", tenantId);

        return client;
    }
}
