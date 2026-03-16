using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;

namespace SmartKb.Api.Tests.Connectors;

public sealed class ConnectorAdminEndpointTests : IClassFixture<ConnectorTestFactory>, IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly ConnectorTestFactory _factory;
    private HttpClient _client = null!;

    public ConnectorAdminEndpointTests(ConnectorTestFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.EnsureDatabaseAsync();
        _client = _factory.CreateAuthenticatedClient();
    }

    public Task DisposeAsync()
    {
        _client?.Dispose();
        return Task.CompletedTask;
    }

    // --- List ---

    [Fact]
    public async Task ListConnectors_ReturnsEmptyList_WhenNoneExist()
    {
        // Use a tenant with no connectors to avoid shared-state interference.
        using var emptyClient = _factory.CreateAuthenticatedClient(tenantId: "tenant-other");
        var response = await emptyClient.GetAsync("/api/admin/connectors");
        response.EnsureSuccessStatusCode();

        var body = await Deserialize<ApiResponse<ConnectorListResponse>>(response);
        Assert.True(body!.IsSuccess);
        Assert.NotNull(body.Data);
        Assert.Empty(body.Data.Connectors);
    }

    [Fact]
    public async Task ListConnectors_ReturnsCreatedConnector()
    {
        var created = await CreateConnectorAsync();
        var response = await _client.GetAsync("/api/admin/connectors");
        var body = await Deserialize<ApiResponse<ConnectorListResponse>>(response);

        Assert.True(body!.IsSuccess);
        Assert.Contains(body.Data!.Connectors, c => c.Id == created.Id);
    }

    // --- Create ---

    [Fact]
    public async Task CreateConnector_ReturnsCreated_WithValidRequest()
    {
        var request = new CreateConnectorRequest
        {
            Name = $"test-ado-{Guid.NewGuid():N}",
            ConnectorType = ConnectorType.AzureDevOps,
            AuthType = SecretAuthType.Pat,
            SourceConfig = """{"orgUrl":"https://dev.azure.com/test"}""",
        };

        var response = await _client.PostAsJsonAsync("/api/admin/connectors", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await Deserialize<ApiResponse<ConnectorResponse>>(response);
        Assert.True(body!.IsSuccess);
        Assert.Equal(request.Name, body.Data!.Name);
        Assert.Equal(ConnectorType.AzureDevOps, body.Data.ConnectorType);
        Assert.Equal(ConnectorStatus.Disabled, body.Data.Status);
        Assert.Equal(SecretAuthType.Pat, body.Data.AuthType);
    }

    [Fact]
    public async Task CreateConnector_Returns422_WhenNameEmpty()
    {
        var request = new CreateConnectorRequest
        {
            Name = "",
            ConnectorType = ConnectorType.AzureDevOps,
            AuthType = SecretAuthType.Pat,
        };

        var response = await _client.PostAsJsonAsync("/api/admin/connectors", request);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task CreateConnector_Returns422_WhenDuplicateName()
    {
        var name = $"dup-{Guid.NewGuid():N}";
        var request = new CreateConnectorRequest
        {
            Name = name,
            ConnectorType = ConnectorType.SharePoint,
            AuthType = SecretAuthType.OAuth,
        };

        await _client.PostAsJsonAsync("/api/admin/connectors", request);
        var response = await _client.PostAsJsonAsync("/api/admin/connectors", request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task CreateConnector_SupportsAllAuthTypes()
    {
        foreach (var authType in Enum.GetValues<SecretAuthType>())
        {
            var request = new CreateConnectorRequest
            {
                Name = $"auth-{authType}-{Guid.NewGuid():N}",
                ConnectorType = ConnectorType.AzureDevOps,
                AuthType = authType,
            };

            var response = await _client.PostAsJsonAsync("/api/admin/connectors", request);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var body = await Deserialize<ApiResponse<ConnectorResponse>>(response);
            Assert.Equal(authType, body!.Data!.AuthType);
        }
    }

    [Fact]
    public async Task CreateConnector_WithFieldMapping()
    {
        var request = new CreateConnectorRequest
        {
            Name = $"mapped-{Guid.NewGuid():N}",
            ConnectorType = ConnectorType.AzureDevOps,
            AuthType = SecretAuthType.Pat,
            FieldMapping = new FieldMappingConfig
            {
                Rules =
                [
                    new FieldMappingRule
                    {
                        SourceField = "System.Title",
                        TargetField = "Title",
                        Transform = FieldTransformType.Direct,
                        IsRequired = true,
                    },
                    new FieldMappingRule
                    {
                        SourceField = "System.Description",
                        TargetField = "TextContent",
                        Transform = FieldTransformType.Direct,
                        IsRequired = true,
                    },
                ],
            },
        };

        var response = await _client.PostAsJsonAsync("/api/admin/connectors", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await Deserialize<ApiResponse<ConnectorResponse>>(response);
        Assert.NotNull(body!.Data!.FieldMapping);
        Assert.Equal(2, body.Data.FieldMapping.Rules.Count);
    }

    // --- Get ---

    [Fact]
    public async Task GetConnector_ReturnsConnector_WhenExists()
    {
        var created = await CreateConnectorAsync();
        var response = await _client.GetAsync($"/api/admin/connectors/{created.Id}");
        response.EnsureSuccessStatusCode();

        var body = await Deserialize<ApiResponse<ConnectorResponse>>(response);
        Assert.Equal(created.Id, body!.Data!.Id);
        Assert.Equal(created.Name, body.Data.Name);
    }

    [Fact]
    public async Task GetConnector_Returns404_WhenNotFound()
    {
        var response = await _client.GetAsync($"/api/admin/connectors/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- Update ---

    [Fact]
    public async Task UpdateConnector_UpdatesName()
    {
        var created = await CreateConnectorAsync();
        var updateRequest = new UpdateConnectorRequest { Name = $"updated-{Guid.NewGuid():N}" };

        var response = await _client.PutAsJsonAsync($"/api/admin/connectors/{created.Id}", updateRequest);
        response.EnsureSuccessStatusCode();

        var body = await Deserialize<ApiResponse<ConnectorResponse>>(response);
        Assert.Equal(updateRequest.Name, body!.Data!.Name);
    }

    [Fact]
    public async Task UpdateConnector_RotatesCredential()
    {
        var created = await CreateConnectorAsync(keyVaultSecretName: "old-secret");
        var updateRequest = new UpdateConnectorRequest { KeyVaultSecretName = "new-rotated-secret" };

        var response = await _client.PutAsJsonAsync($"/api/admin/connectors/{created.Id}", updateRequest);
        response.EnsureSuccessStatusCode();

        var body = await Deserialize<ApiResponse<ConnectorResponse>>(response);
        Assert.True(body!.Data!.HasSecret);
    }

    [Fact]
    public async Task UpdateConnector_Returns404_WhenNotFound()
    {
        var response = await _client.PutAsJsonAsync(
            $"/api/admin/connectors/{Guid.NewGuid()}",
            new UpdateConnectorRequest { Name = "x" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateConnector_Returns422_WhenDuplicateName()
    {
        var c1 = await CreateConnectorAsync();
        var c2 = await CreateConnectorAsync();

        var response = await _client.PutAsJsonAsync(
            $"/api/admin/connectors/{c2.Id}",
            new UpdateConnectorRequest { Name = c1.Name });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // --- Delete ---

    [Fact]
    public async Task DeleteConnector_SoftDeletes()
    {
        var created = await CreateConnectorAsync();
        var response = await _client.DeleteAsync($"/api/admin/connectors/{created.Id}");
        response.EnsureSuccessStatusCode();

        // Should no longer appear in list or get.
        var getResponse = await _client.GetAsync($"/api/admin/connectors/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task DeleteConnector_Returns404_WhenNotFound()
    {
        var response = await _client.DeleteAsync($"/api/admin/connectors/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- Enable / Disable ---

    [Fact]
    public async Task EnableConnector_Returns422_WhenNoFieldMapping()
    {
        var created = await CreateConnectorAsync();
        var response = await _client.PostAsync($"/api/admin/connectors/{created.Id}/enable", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task EnableConnector_Succeeds_WhenRequiredFieldsMapped()
    {
        var created = await CreateConnectorAsync(withFieldMapping: true);
        var response = await _client.PostAsync($"/api/admin/connectors/{created.Id}/enable", null);
        response.EnsureSuccessStatusCode();

        var body = await Deserialize<ApiResponse<ConnectorResponse>>(response);
        Assert.Equal(ConnectorStatus.Enabled, body!.Data!.Status);
    }

    [Fact]
    public async Task DisableConnector_SetsStatusDisabled()
    {
        var created = await CreateConnectorAsync(withFieldMapping: true);
        await _client.PostAsync($"/api/admin/connectors/{created.Id}/enable", null);

        var response = await _client.PostAsync($"/api/admin/connectors/{created.Id}/disable", null);
        response.EnsureSuccessStatusCode();

        var body = await Deserialize<ApiResponse<ConnectorResponse>>(response);
        Assert.Equal(ConnectorStatus.Disabled, body!.Data!.Status);
    }

    // --- Test Connection ---

    [Fact]
    public async Task TestConnection_ReturnsResult_WhenConnectorExists()
    {
        var created = await CreateConnectorAsync();
        var response = await _client.PostAsync($"/api/admin/connectors/{created.Id}/test", null);
        response.EnsureSuccessStatusCode();

        var body = await Deserialize<ApiResponse<TestConnectionResponse>>(response);
        Assert.NotNull(body!.Data);
        // No connector client registered, so it should fail with a diagnostic message.
        Assert.False(body.Data.Success);
        Assert.Contains("No connector client registered", body.Data.Message);
    }

    [Fact]
    public async Task TestConnection_Returns404_WhenNotFound()
    {
        var response = await _client.PostAsync($"/api/admin/connectors/{Guid.NewGuid()}/test", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- Sync Now ---

    [Fact]
    public async Task SyncNow_CreatesSyncRun()
    {
        var created = await CreateConnectorAsync();
        var syncRequest = new SyncNowRequest { IsBackfill = true };

        var response = await _client.PostAsJsonAsync(
            $"/api/admin/connectors/{created.Id}/sync-now", syncRequest);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task SyncNow_Returns404_WhenNotFound()
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/admin/connectors/{Guid.NewGuid()}/sync-now",
            new SyncNowRequest());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- Sync Runs ---

    [Fact]
    public async Task ListSyncRuns_ReturnsRuns_AfterSyncTriggered()
    {
        var created = await CreateConnectorAsync();
        await _client.PostAsJsonAsync(
            $"/api/admin/connectors/{created.Id}/sync-now",
            new SyncNowRequest { IsBackfill = false });

        var response = await _client.GetAsync($"/api/admin/connectors/{created.Id}/sync-runs");
        response.EnsureSuccessStatusCode();

        var body = await Deserialize<ApiResponse<SyncRunListResponse>>(response);
        Assert.NotEmpty(body!.Data!.SyncRuns);
        Assert.Equal(SyncRunStatus.Pending, body.Data.SyncRuns[0].Status);
    }

    [Fact]
    public async Task GetSyncRun_ReturnsSyncRunDetail()
    {
        var created = await CreateConnectorAsync();
        var syncResponse = await _client.PostAsJsonAsync(
            $"/api/admin/connectors/{created.Id}/sync-now",
            new SyncNowRequest());

        var syncBody = await syncResponse.Content.ReadFromJsonAsync<JsonElement>();
        var syncRunId = syncBody.GetProperty("data").GetProperty("syncRunId").GetString();

        var response = await _client.GetAsync(
            $"/api/admin/connectors/{created.Id}/sync-runs/{syncRunId}");
        response.EnsureSuccessStatusCode();

        var body = await Deserialize<ApiResponse<SyncRunSummary>>(response);
        Assert.NotNull(body!.Data);
    }

    // --- Preview ---

    [Fact]
    public async Task Preview_ReturnsResult_WhenConnectorExists()
    {
        var created = await CreateConnectorAsync();
        var response = await _client.PostAsJsonAsync(
            $"/api/admin/connectors/{created.Id}/preview",
            new PreviewRequest { SampleSize = 3 });
        response.EnsureSuccessStatusCode();

        var body = await Deserialize<ApiResponse<PreviewResponse>>(response);
        Assert.NotNull(body!.Data);
        // No connector client, so should have validation errors.
        Assert.NotEmpty(body.Data.ValidationErrors);
    }

    // --- Validate Mapping ---

    [Fact]
    public async Task ValidateMapping_ReturnsValid_ForCorrectMapping()
    {
        var created = await CreateConnectorAsync();
        var mapping = CreateRequiredFieldMapping();

        var response = await _client.PostAsJsonAsync(
            $"/api/admin/connectors/{created.Id}/validate-mapping", mapping);
        response.EnsureSuccessStatusCode();

        var body = await Deserialize<ApiResponse<ConnectorValidationResult>>(response);
        Assert.True(body!.Data!.IsValid);
    }

    [Fact]
    public async Task ValidateMapping_ReturnsInvalid_ForEmptyMapping()
    {
        var created = await CreateConnectorAsync();
        var mapping = new FieldMappingConfig { Rules = [] };

        var response = await _client.PostAsJsonAsync(
            $"/api/admin/connectors/{created.Id}/validate-mapping", mapping);
        response.EnsureSuccessStatusCode();

        var body = await Deserialize<ApiResponse<ConnectorValidationResult>>(response);
        Assert.False(body!.Data!.IsValid);
    }

    // --- RBAC ---

    [Fact]
    public async Task ConnectorEndpoints_Return403_ForNonAdmin()
    {
        var client = _factory.CreateAuthenticatedClient(roles: "SupportAgent");
        var response = await client.GetAsync("/api/admin/connectors");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        client.Dispose();
    }

    [Fact]
    public async Task ConnectorEndpoints_Return401_ForAnonymous()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/admin/connectors");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SyncNow_Return403_ForNonAdmin()
    {
        var client = _factory.CreateAuthenticatedClient(roles: "SupportAgent");
        var response = await client.PostAsJsonAsync(
            $"/api/admin/connectors/{Guid.NewGuid()}/sync-now",
            new SyncNowRequest());
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        client.Dispose();
    }

    // --- Tenant Isolation ---

    [Fact]
    public async Task GetConnector_Returns404_ForOtherTenant()
    {
        var created = await CreateConnectorAsync();

        var otherTenantClient = _factory.CreateAuthenticatedClient(tenantId: "tenant-other");
        var response = await otherTenantClient.GetAsync($"/api/admin/connectors/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        otherTenantClient.Dispose();
    }

    [Fact]
    public async Task ListConnectors_ReturnsOnlyOwnTenant()
    {
        await CreateConnectorAsync();

        var otherClient = _factory.CreateAuthenticatedClient(tenantId: "tenant-other");
        var response = await otherClient.GetAsync("/api/admin/connectors");
        var body = await Deserialize<ApiResponse<ConnectorListResponse>>(response);
        Assert.Empty(body!.Data!.Connectors);
        otherClient.Dispose();
    }

    // --- Helpers ---

    private async Task<ConnectorResponse> CreateConnectorAsync(
        bool withFieldMapping = false,
        string? keyVaultSecretName = null)
    {
        var request = new CreateConnectorRequest
        {
            Name = $"test-{Guid.NewGuid():N}",
            ConnectorType = ConnectorType.AzureDevOps,
            AuthType = SecretAuthType.Pat,
            KeyVaultSecretName = keyVaultSecretName,
            FieldMapping = withFieldMapping ? CreateRequiredFieldMapping() : null,
        };

        var response = await _client.PostAsJsonAsync("/api/admin/connectors", request);
        response.EnsureSuccessStatusCode();

        var body = await Deserialize<ApiResponse<ConnectorResponse>>(response);
        return body!.Data!;
    }

    private static FieldMappingConfig CreateRequiredFieldMapping() => new()
    {
        Rules =
        [
            new FieldMappingRule
            {
                SourceField = "System.Title",
                TargetField = "Title",
                Transform = FieldTransformType.Direct,
                IsRequired = true,
            },
            new FieldMappingRule
            {
                SourceField = "System.Description",
                TargetField = "TextContent",
                Transform = FieldTransformType.Direct,
                IsRequired = true,
            },
            new FieldMappingRule
            {
                SourceField = "System.WorkItemType",
                TargetField = "SourceType",
                Transform = FieldTransformType.Lookup,
                TransformExpression = "Bug=Ticket,Task=Task,Feature=Document",
                IsRequired = true,
            },
        ],
    };

    private static async Task<T?> Deserialize<T>(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }
}
