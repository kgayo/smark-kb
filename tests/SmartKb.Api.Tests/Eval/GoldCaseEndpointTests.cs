using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SmartKb.Api.Tests.Auth;

namespace SmartKb.Api.Tests.Eval;

public class GoldCaseEndpointTests : IAsyncLifetime
{
    private readonly EvalTestFactory _factory = new();
    private HttpClient _adminClient = null!;

    public async Task InitializeAsync()
    {
        await _factory.InitializeAsync();
        await _factory.EnsureDatabaseAsync();
        _adminClient = _factory.CreateAuthenticatedClient(tenantId: "tenant-1", roles: "Admin");
    }

    public async Task DisposeAsync()
    {
        _adminClient.Dispose();
        await _factory.DisposeAsync();
    }

    private static object MakeCreateRequest(string caseId = "eval-00100") => new
    {
        caseId,
        query = "How do I reset my password after lockout?",
        expected = new
        {
            responseType = "final_answer",
            mustInclude = new[] { "password", "reset" },
            mustCiteSources = true,
            shouldHaveEvidence = true,
        },
        tags = new[] { "auth", "password" },
    };

    #region POST /api/admin/eval/gold-cases

    [Fact]
    public async Task CreateGoldCase_ReturnsCreated()
    {
        var response = await _adminClient.PostAsJsonAsync("/api/admin/eval/gold-cases", MakeCreateRequest());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        var data = json.RootElement.GetProperty("data");

        Assert.Equal("eval-00100", data.GetProperty("caseId").GetString());
        Assert.Equal("How do I reset my password after lockout?", data.GetProperty("query").GetString());
        Assert.Equal("final_answer", data.GetProperty("expected").GetProperty("responseType").GetString());
    }

    [Fact]
    public async Task CreateGoldCase_RequiresAdminRole()
    {
        var agentClient = _factory.CreateAuthenticatedClient(tenantId: "tenant-1", roles: "SupportAgent");
        var response = await agentClient.PostAsJsonAsync("/api/admin/eval/gold-cases", MakeCreateRequest());
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateGoldCase_RequiresAuth()
    {
        var anonClient = _factory.CreateClient();
        var response = await anonClient.PostAsJsonAsync("/api/admin/eval/gold-cases", MakeCreateRequest());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion

    #region GET /api/admin/eval/gold-cases

    [Fact]
    public async Task ListGoldCases_ReturnsEmpty()
    {
        var response = await _adminClient.GetAsync("/api/admin/eval/gold-cases");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        var data = json.RootElement.GetProperty("data");

        Assert.Equal(0, data.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, data.GetProperty("cases").GetArrayLength());
    }

    [Fact]
    public async Task ListGoldCases_ReturnsPersisted()
    {
        await _adminClient.PostAsJsonAsync("/api/admin/eval/gold-cases", MakeCreateRequest("eval-00100"));
        await _adminClient.PostAsJsonAsync("/api/admin/eval/gold-cases", MakeCreateRequest("eval-00200"));

        var response = await _adminClient.GetAsync("/api/admin/eval/gold-cases");

        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        var data = json.RootElement.GetProperty("data");

        Assert.Equal(2, data.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, data.GetProperty("cases").GetArrayLength());
    }

    [Fact]
    public async Task ListGoldCases_TenantIsolation()
    {
        await _adminClient.PostAsJsonAsync("/api/admin/eval/gold-cases", MakeCreateRequest());

        var otherClient = _factory.CreateAuthenticatedClient(tenantId: "tenant-other", roles: "Admin");
        var response = await otherClient.GetAsync("/api/admin/eval/gold-cases");

        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        Assert.Equal(0, json.RootElement.GetProperty("data").GetProperty("totalCount").GetInt32());
    }

    #endregion

    #region GET /api/admin/eval/gold-cases/{id}

    [Fact]
    public async Task GetGoldCase_ReturnsDetail()
    {
        var createResponse = await _adminClient.PostAsJsonAsync("/api/admin/eval/gold-cases", MakeCreateRequest());
        var createBody = await createResponse.Content.ReadAsStringAsync();
        var id = JsonDocument.Parse(createBody).RootElement.GetProperty("data").GetProperty("id").GetString();

        var response = await _adminClient.GetAsync($"/api/admin/eval/gold-cases/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        Assert.Equal("eval-00100", json.RootElement.GetProperty("data").GetProperty("caseId").GetString());
    }

    [Fact]
    public async Task GetGoldCase_NotFound_Returns404()
    {
        var response = await _adminClient.GetAsync($"/api/admin/eval/gold-cases/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion

    #region PUT /api/admin/eval/gold-cases/{id}

    [Fact]
    public async Task UpdateGoldCase_ModifiesFields()
    {
        var createResponse = await _adminClient.PostAsJsonAsync("/api/admin/eval/gold-cases", MakeCreateRequest());
        var id = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync())
            .RootElement.GetProperty("data").GetProperty("id").GetString();

        var updateResponse = await _adminClient.PutAsJsonAsync($"/api/admin/eval/gold-cases/{id}", new
        {
            query = "Updated query about password reset",
            tags = new[] { "auth", "sso" },
        });

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var body = await updateResponse.Content.ReadAsStringAsync();
        var data = JsonDocument.Parse(body).RootElement.GetProperty("data");
        Assert.Equal("Updated query about password reset", data.GetProperty("query").GetString());
    }

    [Fact]
    public async Task UpdateGoldCase_NotFound_Returns404()
    {
        var response = await _adminClient.PutAsJsonAsync($"/api/admin/eval/gold-cases/{Guid.NewGuid()}", new { query = "test" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion

    #region DELETE /api/admin/eval/gold-cases/{id}

    [Fact]
    public async Task DeleteGoldCase_RemovesCase()
    {
        var createResponse = await _adminClient.PostAsJsonAsync("/api/admin/eval/gold-cases", MakeCreateRequest());
        var id = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync())
            .RootElement.GetProperty("data").GetProperty("id").GetString();

        var deleteResponse = await _adminClient.DeleteAsync($"/api/admin/eval/gold-cases/{id}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        var getResponse = await _adminClient.GetAsync($"/api/admin/eval/gold-cases/{id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task DeleteGoldCase_NotFound_Returns404()
    {
        var response = await _adminClient.DeleteAsync($"/api/admin/eval/gold-cases/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion

    #region GET /api/admin/eval/gold-cases/export

    [Fact]
    public async Task ExportGoldCases_ReturnsJsonl()
    {
        await _adminClient.PostAsJsonAsync("/api/admin/eval/gold-cases", MakeCreateRequest("eval-00100"));
        await _adminClient.PostAsJsonAsync("/api/admin/eval/gold-cases", MakeCreateRequest("eval-00200"));

        var response = await _adminClient.GetAsync("/api/admin/eval/gold-cases/export");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-ndjson", response.Content.Headers.ContentType?.MediaType);

        var content = await response.Content.ReadAsStringAsync();
        var lines = content.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        Assert.Equal(2, lines.Count);
        Assert.Contains("eval-00100", lines[0]);
    }

    #endregion
}
