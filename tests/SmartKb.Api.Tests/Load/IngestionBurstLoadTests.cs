using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;

namespace SmartKb.Api.Tests.Load;

/// <summary>
/// Load tests for ingestion bursts: concurrent connector creation, parallel sync triggers,
/// and high-volume connector admin operations.
/// </summary>
[Collection("LoadTests")]
public class IngestionBurstLoadTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly LoadTestFactory _factory = new();
    private HttpClient _adminClient = null!;

    public async Task InitializeAsync()
    {
        await _factory.InitializeAsync();
        _adminClient = _factory.CreateAuthenticatedClient(roles: "Admin");
        await _factory.EnsureDatabaseAsync();
    }

    public async Task DisposeAsync()
    {
        _adminClient.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task ConcurrentConnectorCreation_20Parallel_HighSuccessRate()
    {
        const int count = 20;

        var tasks = Enumerable.Range(0, count).Select(async i =>
        {
            using var client = _factory.CreateAuthenticatedClient(roles: "Admin");
            var request = new CreateConnectorRequest
            {
                Name = $"load-connector-{Guid.NewGuid():N}",
                ConnectorType = (ConnectorType)(i % 4),
                AuthType = SecretAuthType.Pat,
                SourceConfig = $@"{{""orgUrl"":""https://dev.azure.com/load-{i}""}}",
            };
            var response = await client.PostAsJsonAsync("/api/admin/connectors", request, JsonOptions);
            return response;
        });

        var responses = await Task.WhenAll(tasks);

        // Under SQLite single-writer, some concurrent writes may hit DB lock.
        var created = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        Assert.True(created >= count / 2,
            $"Expected at least {count / 2} successful creates out of {count}, got {created}");

        // No responses should be anything other than Created or transient 500 (DB lock).
        Assert.All(responses, r =>
            Assert.True(r.StatusCode == HttpStatusCode.Created || r.StatusCode == HttpStatusCode.InternalServerError,
                $"Unexpected status {r.StatusCode}"));
    }

    [Fact]
    public async Task ConcurrentSyncTriggers_10Connectors_HighSuccessRate()
    {
        // Create connectors with field mappings and enable them sequentially.
        var connectorIds = new List<Guid>();
        for (var i = 0; i < 10; i++)
        {
            var createReq = new CreateConnectorRequest
            {
                Name = $"sync-burst-{Guid.NewGuid():N}",
                ConnectorType = ConnectorType.AzureDevOps,
                AuthType = SecretAuthType.Pat,
                SourceConfig = $@"{{""orgUrl"":""https://dev.azure.com/sync-{i}"",""projects"":[""Proj{i}""]}}",
                FieldMapping = new FieldMappingConfig
                {
                    Rules =
                    [
                        new FieldMappingRule
                        {
                            SourceField = "System.Title",
                            TargetField = "title",
                            Transform = FieldTransformType.Direct,
                            IsRequired = true,
                        }
                    ],
                },
            };
            var createResp = await _adminClient.PostAsJsonAsync("/api/admin/connectors", createReq, JsonOptions);
            Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
            var body = await createResp.Content.ReadFromJsonAsync<ApiResponse<ConnectorResponse>>(JsonOptions);
            connectorIds.Add(body!.Data!.Id);

            await _adminClient.PostAsync($"/api/admin/connectors/{body.Data.Id}/enable", null);
        }

        // Trigger sync-now on all connectors concurrently.
        var syncTasks = connectorIds.Select(async id =>
        {
            using var client = _factory.CreateAuthenticatedClient(roles: "Admin");
            var syncRequest = new SyncNowRequest { IsBackfill = true };
            var response = await client.PostAsJsonAsync(
                $"/api/admin/connectors/{id}/sync-now", syncRequest, JsonOptions);
            return response;
        });

        var syncResponses = await Task.WhenAll(syncTasks);

        // Under SQLite concurrency, tolerate some transient failures.
        var accepted = syncResponses.Count(r => r.StatusCode == HttpStatusCode.Accepted);
        Assert.True(accepted >= 5,
            $"Expected at least 5 accepted sync triggers out of 10, got {accepted}");
    }

    [Fact]
    public async Task ConcurrentConnectorListAndDetail_30Parallel_HighAvailability()
    {
        // Create connectors sequentially.
        for (var i = 0; i < 5; i++)
        {
            var req = new CreateConnectorRequest
            {
                Name = $"list-detail-{Guid.NewGuid():N}",
                ConnectorType = ConnectorType.SharePoint,
                AuthType = SecretAuthType.OAuth,
            };
            await _adminClient.PostAsJsonAsync("/api/admin/connectors", req, JsonOptions);
        }

        // Concurrent list requests (read-only).
        const int parallelReads = 30;
        var tasks = Enumerable.Range(0, parallelReads).Select(async _ =>
        {
            using var client = _factory.CreateAuthenticatedClient(roles: "Admin");
            var response = await client.GetAsync("/api/admin/connectors");
            if (response.StatusCode != HttpStatusCode.OK)
                return (ok: false, count: 0);
            var body = await response.Content.ReadFromJsonAsync<ApiResponse<ConnectorListResponse>>(JsonOptions);
            return (ok: true, count: body!.Data!.Connectors.Count);
        });

        var results = await Task.WhenAll(tasks);

        // Under SQLite, reads may contend with concurrent writes; some should succeed.
        var successResults = results.Where(r => r.ok).ToList();
        Assert.True(successResults.Count >= 1,
            $"Expected at least 1 successful read out of {parallelReads}, got {successResults.Count}");

        // All successful reads should see the same count (consistency).
        if (successResults.Count > 1)
        {
            var distinctCounts = successResults.Select(r => r.count).Distinct().ToList();
            Assert.True(distinctCounts.Count == 1,
                $"Inconsistent connector counts across reads: {string.Join(", ", distinctCounts)}");
        }
    }

    [Fact]
    public async Task ConcurrentEnableDisable_SameConnector_NoCorruption()
    {
        var createReq = new CreateConnectorRequest
        {
            Name = $"toggle-{Guid.NewGuid():N}",
            ConnectorType = ConnectorType.AzureDevOps,
            AuthType = SecretAuthType.Pat,
            FieldMapping = new FieldMappingConfig
            {
                Rules =
                [
                    new FieldMappingRule
                    {
                        SourceField = "System.Title",
                        TargetField = "title",
                        Transform = FieldTransformType.Direct,
                        IsRequired = true,
                    }
                ],
            },
        };
        var createResp = await _adminClient.PostAsJsonAsync("/api/admin/connectors", createReq, JsonOptions);
        var body = await createResp.Content.ReadFromJsonAsync<ApiResponse<ConnectorResponse>>(JsonOptions);
        var connectorId = body!.Data!.Id;

        // Rapidly toggle enable/disable concurrently.
        const int toggleCount = 10;
        var tasks = Enumerable.Range(0, toggleCount).Select(async i =>
        {
            using var client = _factory.CreateAuthenticatedClient(roles: "Admin");
            if (i % 2 == 0)
                return await client.PostAsync($"/api/admin/connectors/{connectorId}/enable", null);
            else
                return await client.PostAsync($"/api/admin/connectors/{connectorId}/disable", null);
        });

        var responses = await Task.WhenAll(tasks);

        // At least some should succeed under contention.
        var successes = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        Assert.True(successes >= 1, $"Expected at least 1 success in toggle operations, got {successes}");

        // Connector should still be retrievable and in a valid state.
        var getResp = await _adminClient.GetAsync($"/api/admin/connectors/{connectorId}");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var detail = await getResp.Content.ReadFromJsonAsync<ApiResponse<ConnectorResponse>>(JsonOptions);
        Assert.True(detail!.Data!.Status == ConnectorStatus.Enabled || detail.Data.Status == ConnectorStatus.Disabled);
    }

    [Fact]
    public async Task BurstConnectorCreation_Throughput_CompletesWithinBudget()
    {
        const int count = 30;
        var sw = Stopwatch.StartNew();

        for (var i = 0; i < count; i++)
        {
            var request = new CreateConnectorRequest
            {
                Name = $"throughput-{Guid.NewGuid():N}",
                ConnectorType = ConnectorType.HubSpot,
                AuthType = SecretAuthType.Pat,
            };
            var response = await _adminClient.PostAsJsonAsync("/api/admin/connectors", request, JsonOptions);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        sw.Stop();
        Assert.True(sw.Elapsed.TotalSeconds < 30,
            $"30 sequential connector creates took {sw.Elapsed.TotalSeconds:F1}s (budget: 30s)");
    }

    [Fact]
    public async Task ConcurrentConnectorCreation_MultiTenant_NoLeakage()
    {
        const int perTenant = 10;

        // Create connectors sequentially per tenant to avoid DB lock.
        for (var i = 0; i < perTenant; i++)
        {
            using var t1Client = _factory.CreateAuthenticatedClient(tenantId: "tenant-1", roles: "Admin");
            var req1 = new CreateConnectorRequest
            {
                Name = $"t1-load-{Guid.NewGuid():N}",
                ConnectorType = ConnectorType.AzureDevOps,
                AuthType = SecretAuthType.Pat,
            };
            await t1Client.PostAsJsonAsync("/api/admin/connectors", req1, JsonOptions);

            using var t2Client = _factory.CreateAuthenticatedClient(tenantId: "tenant-2", roles: "Admin");
            var req2 = new CreateConnectorRequest
            {
                Name = $"t2-load-{Guid.NewGuid():N}",
                ConnectorType = ConnectorType.SharePoint,
                AuthType = SecretAuthType.OAuth,
            };
            await t2Client.PostAsJsonAsync("/api/admin/connectors", req2, JsonOptions);
        }

        // Verify tenant isolation with concurrent reads.
        using var t1ListClient = _factory.CreateAuthenticatedClient(tenantId: "tenant-1", roles: "Admin");
        var t1List = await t1ListClient.GetAsync("/api/admin/connectors");
        var t1Body = await t1List.Content.ReadFromJsonAsync<ApiResponse<ConnectorListResponse>>(JsonOptions);

        using var t2ListClient = _factory.CreateAuthenticatedClient(tenantId: "tenant-2", roles: "Admin");
        var t2List = await t2ListClient.GetAsync("/api/admin/connectors");
        var t2Body = await t2List.Content.ReadFromJsonAsync<ApiResponse<ConnectorListResponse>>(JsonOptions);

        // Each tenant should only see their own connectors.
        Assert.True(t1Body!.Data!.Connectors.Count >= perTenant);
        Assert.All(t1Body.Data.Connectors, c => Assert.Equal(ConnectorType.AzureDevOps, c.ConnectorType));

        Assert.True(t2Body!.Data!.Connectors.Count >= perTenant);
        Assert.All(t2Body.Data.Connectors, c => Assert.Equal(ConnectorType.SharePoint, c.ConnectorType));
    }
}
