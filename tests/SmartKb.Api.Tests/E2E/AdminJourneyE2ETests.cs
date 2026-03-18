using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;

namespace SmartKb.Api.Tests.E2E;

/// <summary>
/// End-to-end test for the admin connector journey:
/// create connector → configure field mapping → validate mapping → test connection →
/// enable connector → trigger sync → check sync runs → query evidence →
/// disable connector → delete connector.
/// Also tests role enforcement across the journey.
/// </summary>
public class AdminJourneyE2ETests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly E2ETestFactory _factory = new();
    private HttpClient _adminClient = null!;
    private HttpClient _agentClient = null!;

    public async Task InitializeAsync()
    {
        await _factory.InitializeAsync();
        _adminClient = _factory.CreateAuthenticatedClient(roles: "Admin");
        _agentClient = _factory.CreateAuthenticatedClient(roles: "SupportAgent");
        await _factory.EnsureDatabaseAsync();
    }

    public async Task DisposeAsync()
    {
        _adminClient.Dispose();
        _agentClient.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task FullAdminJourney_ConnectToDelete()
    {
        var connectorName = $"e2e-ado-{Guid.NewGuid():N}";

        // Step 1: Create a connector.
        var createRequest = new CreateConnectorRequest
        {
            Name = connectorName,
            ConnectorType = ConnectorType.AzureDevOps,
            AuthType = SecretAuthType.Pat,
            SourceConfig = """{"orgUrl":"https://dev.azure.com/e2e-test","projects":["TestProject"]}""",
        };

        var createResponse = await _adminClient.PostAsJsonAsync("/api/admin/connectors", createRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var connector = await Deserialize<ApiResponse<ConnectorResponse>>(createResponse);
        Assert.True(connector!.IsSuccess);
        Assert.Equal(connectorName, connector.Data!.Name);
        Assert.Equal(ConnectorType.AzureDevOps, connector.Data.ConnectorType);
        Assert.Equal(ConnectorStatus.Disabled, connector.Data.Status);
        Assert.Equal(SecretAuthType.Pat, connector.Data.AuthType);
        var connectorId = connector.Data.Id;

        // Step 2: Verify connector appears in list.
        var listResponse = await _adminClient.GetAsync("/api/admin/connectors");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var connectorList = await Deserialize<ApiResponse<ConnectorListResponse>>(listResponse);
        Assert.Contains(connectorList!.Data!.Connectors, c => c.Id == connectorId);

        // Step 3: Get connector detail.
        var getResponse = await _adminClient.GetAsync($"/api/admin/connectors/{connectorId}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var connectorDetail = await Deserialize<ApiResponse<ConnectorResponse>>(getResponse);
        Assert.Equal(connectorId, connectorDetail!.Data!.Id);
        Assert.Equal(connectorName, connectorDetail.Data.Name);

        // Step 4: Update connector with field mapping.
        var fieldMapping = CreateRequiredFieldMapping();
        var updateRequest = new UpdateConnectorRequest
        {
            FieldMapping = fieldMapping,
        };

        var updateResponse = await _adminClient.PutAsJsonAsync($"/api/admin/connectors/{connectorId}", updateRequest);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var updated = await Deserialize<ApiResponse<ConnectorResponse>>(updateResponse);
        Assert.NotNull(updated!.Data!.FieldMapping);
        Assert.Equal(3, updated.Data.FieldMapping.Rules.Count);

        // Step 5: Validate the field mapping.
        var validateResponse = await _adminClient.PostAsJsonAsync(
            $"/api/admin/connectors/{connectorId}/validate-mapping", fieldMapping);
        Assert.Equal(HttpStatusCode.OK, validateResponse.StatusCode);

        var validation = await Deserialize<ApiResponse<ConnectorValidationResult>>(validateResponse);
        Assert.True(validation!.Data!.IsValid);

        // Step 6: Validate that invalid mapping is rejected.
        var invalidMapping = new FieldMappingConfig { Rules = [] };
        var invalidValidateResponse = await _adminClient.PostAsJsonAsync(
            $"/api/admin/connectors/{connectorId}/validate-mapping", invalidMapping);
        Assert.Equal(HttpStatusCode.OK, invalidValidateResponse.StatusCode);

        var invalidValidation = await Deserialize<ApiResponse<ConnectorValidationResult>>(invalidValidateResponse);
        Assert.False(invalidValidation!.Data!.IsValid);

        // Step 7: Test connection (will fail because no real ADO behind it, but endpoint works).
        var testResponse = await _adminClient.PostAsync($"/api/admin/connectors/{connectorId}/test", null);
        Assert.Equal(HttpStatusCode.OK, testResponse.StatusCode);

        var testResult = await Deserialize<ApiResponse<TestConnectionResponse>>(testResponse);
        Assert.NotNull(testResult!.Data);
        Assert.False(testResult.Data.Success); // Expected — no real ADO.

        // Step 8: Enable connector (requires field mapping — already set).
        var enableResponse = await _adminClient.PostAsync($"/api/admin/connectors/{connectorId}/enable", null);
        Assert.Equal(HttpStatusCode.OK, enableResponse.StatusCode);

        var enabled = await Deserialize<ApiResponse<ConnectorResponse>>(enableResponse);
        Assert.Equal(ConnectorStatus.Enabled, enabled!.Data!.Status);

        // Step 9: Trigger sync (backfill).
        var syncResponse = await _adminClient.PostAsJsonAsync(
            $"/api/admin/connectors/{connectorId}/sync-now",
            new SyncNowRequest { IsBackfill = true });
        Assert.Equal(HttpStatusCode.Accepted, syncResponse.StatusCode);

        // Step 10: Check sync runs.
        var syncRunsResponse = await _adminClient.GetAsync($"/api/admin/connectors/{connectorId}/sync-runs");
        Assert.Equal(HttpStatusCode.OK, syncRunsResponse.StatusCode);

        var syncRuns = await Deserialize<ApiResponse<SyncRunListResponse>>(syncRunsResponse);
        Assert.NotEmpty(syncRuns!.Data!.SyncRuns);
        Assert.Equal(SyncRunStatus.Pending, syncRuns.Data.SyncRuns[0].Status);

        // Step 11: Get specific sync run detail.
        var syncRunId = syncRuns.Data.SyncRuns[0].Id;
        var syncRunDetailResponse = await _adminClient.GetAsync(
            $"/api/admin/connectors/{connectorId}/sync-runs/{syncRunId}");
        Assert.Equal(HttpStatusCode.OK, syncRunDetailResponse.StatusCode);

        var syncRunDetail = await Deserialize<ApiResponse<SyncRunSummary>>(syncRunDetailResponse);
        Assert.NotNull(syncRunDetail!.Data);
        Assert.Equal(syncRunId, syncRunDetail.Data.Id);

        // Step 12: Trigger incremental sync.
        var incrementalSyncResponse = await _adminClient.PostAsJsonAsync(
            $"/api/admin/connectors/{connectorId}/sync-now",
            new SyncNowRequest { IsBackfill = false });
        Assert.Equal(HttpStatusCode.Accepted, incrementalSyncResponse.StatusCode);

        // Verify 2 sync runs now.
        var allSyncRunsResponse = await _adminClient.GetAsync($"/api/admin/connectors/{connectorId}/sync-runs");
        var allSyncRuns = await Deserialize<ApiResponse<SyncRunListResponse>>(allSyncRunsResponse);
        Assert.True(allSyncRuns!.Data!.SyncRuns.Count >= 2);

        // Step 13: Agent can chat (sessions work even with connectors configured).
        var sessionResponse = await _agentClient.PostAsJsonAsync("/api/sessions",
            new CreateSessionRequest { Title = "Agent query post-sync" });
        Assert.Equal(HttpStatusCode.Created, sessionResponse.StatusCode);

        var session = (await sessionResponse.Content.ReadFromJsonAsync<ApiResponse<SessionResponse>>())!.Data!;

        var chatResponse = await _agentClient.PostAsJsonAsync($"/api/sessions/{session.SessionId}/messages",
            new SendMessageRequest { Query = "Show me recent deployment issues" });
        Assert.Equal(HttpStatusCode.OK, chatResponse.StatusCode);

        var chatResult = (await chatResponse.Content.ReadFromJsonAsync<ApiResponse<SessionChatResponse>>())!.Data!;
        Assert.NotEmpty(chatResult.ChatResponse.Answer);

        // Step 14: Disable connector.
        var disableResponse = await _adminClient.PostAsync($"/api/admin/connectors/{connectorId}/disable", null);
        Assert.Equal(HttpStatusCode.OK, disableResponse.StatusCode);

        var disabled = await Deserialize<ApiResponse<ConnectorResponse>>(disableResponse);
        Assert.Equal(ConnectorStatus.Disabled, disabled!.Data!.Status);

        // Step 15: Delete connector (soft-delete).
        var deleteResponse = await _adminClient.DeleteAsync($"/api/admin/connectors/{connectorId}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        // Step 16: Verify connector no longer accessible.
        var getDeletedResponse = await _adminClient.GetAsync($"/api/admin/connectors/{connectorId}");
        Assert.Equal(HttpStatusCode.NotFound, getDeletedResponse.StatusCode);
    }

    [Fact]
    public async Task AdminJourney_RoleEnforcement()
    {
        // Agent cannot access admin endpoints.
        var listResponse = await _agentClient.GetAsync("/api/admin/connectors");
        Assert.Equal(HttpStatusCode.Forbidden, listResponse.StatusCode);

        var createResponse = await _agentClient.PostAsJsonAsync("/api/admin/connectors",
            new CreateConnectorRequest
            {
                Name = "blocked",
                ConnectorType = ConnectorType.AzureDevOps,
                AuthType = SecretAuthType.Pat,
            });
        Assert.Equal(HttpStatusCode.Forbidden, createResponse.StatusCode);

        var syncResponse = await _agentClient.PostAsJsonAsync(
            $"/api/admin/connectors/{Guid.NewGuid()}/sync-now",
            new SyncNowRequest());
        Assert.Equal(HttpStatusCode.Forbidden, syncResponse.StatusCode);

        // Anonymous cannot access admin endpoints.
        var anonClient = _factory.CreateClient();
        var anonResponse = await anonClient.GetAsync("/api/admin/connectors");
        Assert.Equal(HttpStatusCode.Unauthorized, anonResponse.StatusCode);
        anonClient.Dispose();
    }

    [Fact]
    public async Task AdminJourney_TenantIsolation()
    {
        // Create connector in tenant-1.
        var createResponse = await _adminClient.PostAsJsonAsync("/api/admin/connectors",
            new CreateConnectorRequest
            {
                Name = $"iso-{Guid.NewGuid():N}",
                ConnectorType = ConnectorType.SharePoint,
                AuthType = SecretAuthType.OAuth,
                SourceConfig = """{"siteUrl":"https://contoso.sharepoint.com"}""",
            });
        var connector = (await Deserialize<ApiResponse<ConnectorResponse>>(createResponse))!.Data!;

        // Admin in tenant-2 cannot see tenant-1's connector.
        var otherAdminClient = _factory.CreateAuthenticatedClient(
            tenantId: "tenant-2", roles: "Admin", userId: "other-admin");

        var getResponse = await otherAdminClient.GetAsync($"/api/admin/connectors/{connector.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);

        var listResponse = await otherAdminClient.GetAsync("/api/admin/connectors");
        var list = await Deserialize<ApiResponse<ConnectorListResponse>>(listResponse);
        Assert.DoesNotContain(list!.Data!.Connectors, c => c.Id == connector.Id);

        otherAdminClient.Dispose();
    }

    [Fact]
    public async Task AdminJourney_EnableRequiresFieldMapping()
    {
        // Create connector without field mapping.
        var createResponse = await _adminClient.PostAsJsonAsync("/api/admin/connectors",
            new CreateConnectorRequest
            {
                Name = $"no-map-{Guid.NewGuid():N}",
                ConnectorType = ConnectorType.AzureDevOps,
                AuthType = SecretAuthType.Pat,
            });
        var connector = (await Deserialize<ApiResponse<ConnectorResponse>>(createResponse))!.Data!;

        // Attempt to enable without field mapping — should fail.
        var enableResponse = await _adminClient.PostAsync($"/api/admin/connectors/{connector.Id}/enable", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, enableResponse.StatusCode);

        // Add field mapping.
        await _adminClient.PutAsJsonAsync($"/api/admin/connectors/{connector.Id}",
            new UpdateConnectorRequest { FieldMapping = CreateRequiredFieldMapping() });

        // Now enable should succeed.
        var enableAfterMappingResponse = await _adminClient.PostAsync(
            $"/api/admin/connectors/{connector.Id}/enable", null);
        Assert.Equal(HttpStatusCode.OK, enableAfterMappingResponse.StatusCode);

        var enabled = await Deserialize<ApiResponse<ConnectorResponse>>(enableAfterMappingResponse);
        Assert.Equal(ConnectorStatus.Enabled, enabled!.Data!.Status);
    }

    [Fact]
    public async Task AdminJourney_MultipleConnectorTypes()
    {
        // Create connectors of different types.
        var types = new[]
        {
            (ConnectorType.AzureDevOps, SecretAuthType.Pat),
            (ConnectorType.SharePoint, SecretAuthType.OAuth),
            (ConnectorType.HubSpot, SecretAuthType.Pat),
            (ConnectorType.ClickUp, SecretAuthType.Pat),
        };

        var createdIds = new List<Guid>();

        foreach (var (connType, authType) in types)
        {
            var response = await _adminClient.PostAsJsonAsync("/api/admin/connectors",
                new CreateConnectorRequest
                {
                    Name = $"multi-{connType}-{Guid.NewGuid():N}",
                    ConnectorType = connType,
                    AuthType = authType,
                });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var conn = (await Deserialize<ApiResponse<ConnectorResponse>>(response))!.Data!;
            createdIds.Add(conn.Id);
        }

        // List — all 4 appear.
        var listResponse = await _adminClient.GetAsync("/api/admin/connectors");
        var list = await Deserialize<ApiResponse<ConnectorListResponse>>(listResponse);

        foreach (var id in createdIds)
        {
            Assert.Contains(list!.Data!.Connectors, c => c.Id == id);
        }
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
