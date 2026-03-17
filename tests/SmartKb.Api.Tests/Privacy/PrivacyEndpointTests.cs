using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using SmartKb.Api.Tests.Auth;

namespace SmartKb.Api.Tests.Privacy;

public sealed class PrivacyEndpointTests : IClassFixture<AuthTestFactory>
{
    private readonly AuthTestFactory _factory;

    public PrivacyEndpointTests(AuthTestFactory factory)
    {
        _factory = factory;
    }

    #region PII Policy Endpoints

    [Fact]
    public async Task GetPiiPolicy_NoPolicy_ReturnsOkWithMessage()
    {
        var client = CreateAdminClient("tenant-priv");

        var response = await client.GetAsync("/api/admin/privacy/pii-policy");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        Assert.True(json.RootElement.GetProperty("data").TryGetProperty("message", out _));
    }

    [Fact]
    public async Task PutPiiPolicy_ValidRequest_ReturnsPolicy()
    {
        var client = CreateAdminClient("tenant-priv");

        var request = new
        {
            enforcementMode = "redact",
            enabledPiiTypes = new[] { "email", "ssn" },
            auditRedactions = true,
        };

        var response = await client.PutAsJsonAsync("/api/admin/privacy/pii-policy", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        var data = json.RootElement.GetProperty("data");

        Assert.Equal("redact", data.GetProperty("enforcementMode").GetString());
        Assert.Equal(2, data.GetProperty("enabledPiiTypes").GetArrayLength());
    }

    [Fact]
    public async Task PutPiiPolicy_InvalidMode_ReturnsBadRequest()
    {
        var client = CreateAdminClient("tenant-priv");

        var request = new
        {
            enforcementMode = "bogus",
            enabledPiiTypes = new[] { "email" },
        };

        var response = await client.PutAsJsonAsync("/api/admin/privacy/pii-policy", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeletePiiPolicy_AfterCreate_ReturnsOk()
    {
        var client = CreateAdminClient("tenant-priv");

        await client.PutAsJsonAsync("/api/admin/privacy/pii-policy", new
        {
            enforcementMode = "detect",
            enabledPiiTypes = new[] { "email" },
        });

        var response = await client.DeleteAsync("/api/admin/privacy/pii-policy");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PiiPolicy_RequiresPrivacyManagePermission()
    {
        var client = CreateClient("SupportAgent", "tenant-priv-rbac");

        var response = await client.GetAsync("/api/admin/privacy/pii-policy");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    #endregion

    #region Retention Policy Endpoints

    [Fact]
    public async Task GetRetention_Empty_ReturnsOk()
    {
        var client = CreateAdminClient("tenant-priv-ret");

        var response = await client.GetAsync("/api/admin/privacy/retention");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        var data = json.RootElement.GetProperty("data");
        Assert.Equal(0, data.GetProperty("policies").GetArrayLength());
    }

    [Fact]
    public async Task PutRetention_ValidRequest_ReturnsPolicy()
    {
        var client = CreateAdminClient("tenant-priv-ret");

        var request = new { entityType = "AppSession", retentionDays = 90 };
        var response = await client.PutAsJsonAsync("/api/admin/privacy/retention", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        var data = json.RootElement.GetProperty("data");
        Assert.Equal("AppSession", data.GetProperty("entityType").GetString());
        Assert.Equal(90, data.GetProperty("retentionDays").GetInt32());
    }

    [Fact]
    public async Task PutRetention_InvalidEntityType_ReturnsBadRequest()
    {
        var client = CreateAdminClient("tenant-priv-ret");

        var request = new { entityType = "Invalid", retentionDays = 30 };
        var response = await client.PutAsJsonAsync("/api/admin/privacy/retention", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeleteRetention_ExistingType_ReturnsOk()
    {
        var client = CreateAdminClient("tenant-priv-ret");

        await client.PutAsJsonAsync("/api/admin/privacy/retention",
            new { entityType = "Message", retentionDays = 60 });

        var response = await client.DeleteAsync("/api/admin/privacy/retention/Message");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DeleteRetention_NotFound_ReturnsNotFound()
    {
        var client = CreateAdminClient("tenant-priv-ret");

        var response = await client.DeleteAsync("/api/admin/privacy/retention/AuditEvent");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RetentionCleanup_ReturnsResults()
    {
        var client = CreateAdminClient("tenant-priv-ret");

        var response = await client.PostAsync("/api/admin/privacy/retention/cleanup", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Retention_RequiresPrivacyManagePermission()
    {
        var client = CreateClient("SupportAgent", "tenant-priv-rbac");

        var response = await client.GetAsync("/api/admin/privacy/retention");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    #endregion

    #region Data Subject Deletion Endpoints

    [Fact]
    public async Task DataSubjectDeletion_RequestAndList()
    {
        var client = CreateAdminClient("tenant-priv-del");

        var request = new { subjectId = "user-to-delete" };
        var response = await client.PostAsJsonAsync("/api/admin/privacy/data-subject-deletion", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        var data = json.RootElement.GetProperty("data");
        Assert.Equal("Completed", data.GetProperty("status").GetString());
        Assert.Equal("user-to-delete", data.GetProperty("subjectId").GetString());

        // List requests.
        var listResponse = await client.GetAsync("/api/admin/privacy/data-subject-deletion");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var listBody = await listResponse.Content.ReadAsStringAsync();
        var listJson = JsonDocument.Parse(listBody);
        var listData = listJson.RootElement.GetProperty("data");
        Assert.True(listData.GetProperty("totalCount").GetInt32() >= 1);
    }

    [Fact]
    public async Task DataSubjectDeletion_GetById_NotFound()
    {
        var client = CreateAdminClient("tenant-priv-del");

        var response = await client.GetAsync($"/api/admin/privacy/data-subject-deletion/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DataSubjectDeletion_RequiresPrivacyManagePermission()
    {
        var client = CreateClient("SupportAgent", "tenant-priv-rbac");

        var response = await client.GetAsync("/api/admin/privacy/data-subject-deletion");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    #endregion

    private HttpClient CreateAdminClient(string tenantId) => CreateClient("Admin", tenantId);

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
