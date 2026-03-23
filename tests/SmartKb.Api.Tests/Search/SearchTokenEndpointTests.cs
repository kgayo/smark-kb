using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using SmartKb.Api.Tests.Auth;

namespace SmartKb.Api.Tests.Search;

public sealed class SearchTokenEndpointTests : IClassFixture<AuthTestFactory>
{
    private readonly AuthTestFactory _factory;
    private const string TenantId = "tenant-1";

    public SearchTokenEndpointTests(AuthTestFactory factory)
    {
        _factory = factory;
    }

    // ==================== Stop Words ====================

    [Fact]
    public async Task ListStopWords_ReturnsEmptyList()
    {
        var client = CreateAdminClient("tenant-other");
        var response = await client.GetAsync("/api/admin/stop-words");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await ParseData(response);
        Assert.Equal(0, json.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task CreateStopWord_Returns201()
    {
        var client = CreateAdminClient(TenantId);
        var response = await client.PostAsJsonAsync("/api/admin/stop-words",
            new { word = "testword", groupName = "test-group" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var json = await ParseData(response);
        Assert.Equal("testword", json.GetProperty("word").GetString());
        Assert.Equal("test-group", json.GetProperty("groupName").GetString());
    }

    [Fact]
    public async Task CreateStopWord_EmptyWord_Returns422()
    {
        var client = CreateAdminClient(TenantId);
        var response = await client.PostAsJsonAsync("/api/admin/stop-words",
            new { word = "", groupName = "general" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task DeleteStopWord_NotFound_Returns404()
    {
        var client = CreateAdminClient(TenantId);
        var response = await client.DeleteAsync($"/api/admin/stop-words/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SeedStopWords_ReturnsCount()
    {
        var client = CreateAdminClient("tenant-ret");
        var response = await client.PostAsJsonAsync("/api/admin/stop-words/seed",
            new { overwriteExisting = false });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await ParseData(response);
        Assert.True(json.GetProperty("seeded").GetInt32() > 0);
    }

    [Fact]
    public async Task StopWords_SupportAgent_Returns403()
    {
        var client = CreateClient("SupportAgent", TenantId);
        var response = await client.GetAsync("/api/admin/stop-words");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ==================== Special Tokens ====================

    [Fact]
    public async Task ListSpecialTokens_ReturnsEmptyList()
    {
        var client = CreateAdminClient("tenant-other");
        var response = await client.GetAsync("/api/admin/special-tokens");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await ParseData(response);
        Assert.Equal(0, json.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task CreateSpecialToken_Returns201()
    {
        var client = CreateAdminClient(TenantId);
        var response = await client.PostAsJsonAsync("/api/admin/special-tokens",
            new { token = "TEST-TOKEN-001", category = "test-cat", boostFactor = 3, description = "Test token" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var json = await ParseData(response);
        Assert.Equal("TEST-TOKEN-001", json.GetProperty("token").GetString());
        Assert.Equal(3, json.GetProperty("boostFactor").GetInt32());
    }

    [Fact]
    public async Task CreateSpecialToken_InvalidBoost_Returns422()
    {
        var client = CreateAdminClient(TenantId);
        var response = await client.PostAsJsonAsync("/api/admin/special-tokens",
            new { token = "BAD-BOOST", boostFactor = 99 });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task DeleteSpecialToken_NotFound_Returns404()
    {
        var client = CreateAdminClient(TenantId);
        var response = await client.DeleteAsync($"/api/admin/special-tokens/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SeedSpecialTokens_ReturnsCount()
    {
        var client = CreateAdminClient("tenant-ret");
        var response = await client.PostAsJsonAsync("/api/admin/special-tokens/seed",
            new { overwriteExisting = false });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await ParseData(response);
        Assert.True(json.GetProperty("seeded").GetInt32() > 0);
    }

    [Fact]
    public async Task SpecialTokens_SupportAgent_Returns403()
    {
        var client = CreateClient("SupportAgent", TenantId);
        var response = await client.GetAsync("/api/admin/special-tokens");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ==================== Helpers ====================

    private static async Task<JsonElement> ParseData(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("data");
    }

    private HttpClient CreateAdminClient(string tenantId) => CreateClient("Admin", tenantId);

    private HttpClient CreateClient(string role, string tenantId)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Test-Auth", "true");
        client.DefaultRequestHeaders.Add("X-Test-Roles", role);
        client.DefaultRequestHeaders.Add("X-Test-Tenant", tenantId);
        return client;
    }
}
