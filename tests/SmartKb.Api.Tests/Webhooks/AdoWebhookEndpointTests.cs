using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartKb.Api.Tests.Connectors;
using SmartKb.Contracts.Enums;
using SmartKb.Data;
using SmartKb.Data.Entities;

namespace SmartKb.Api.Tests.Webhooks;

public sealed class AdoWebhookEndpointTests : IClassFixture<ConnectorTestFactory>, IAsyncLifetime
{
    private readonly ConnectorTestFactory _factory;
    private HttpClient _client = null!;
    private Guid _connectorId;

    public AdoWebhookEndpointTests(ConnectorTestFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.EnsureDatabaseAsync();
        _client = _factory.CreateClient(); // Unauthenticated — webhook endpoint is anonymous.

        // Seed a connector and webhook subscription for the endpoint test.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmartKbDbContext>();

        _connectorId = Guid.NewGuid();
        db.Connectors.Add(new ConnectorEntity
        {
            Id = _connectorId,
            TenantId = "tenant-1",
            Name = $"webhook-test-{Guid.NewGuid():N}",
            ConnectorType = ConnectorType.AzureDevOps,
            Status = ConnectorStatus.Enabled,
            AuthType = SecretAuthType.Pat,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        // Add subscription with no secret (skip signature verification in this test).
        db.Set<WebhookSubscriptionEntity>().Add(new WebhookSubscriptionEntity
        {
            Id = Guid.NewGuid(),
            ConnectorId = _connectorId,
            TenantId = "tenant-1",
            ExternalSubscriptionId = "ext-1",
            EventType = "workitem.updated",
            CallbackUrl = $"https://test/api/webhooks/ado/{_connectorId}",
            WebhookSecretName = null,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync();
    }

    public Task DisposeAsync()
    {
        _client?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task WebhookEndpoint_IsAnonymous_NoAuthRequired()
    {
        var payload = BuildPayload("workitem.updated");
        var content = new StringContent(payload, Encoding.UTF8, "application/json");

        var response = await _client.PostAsync($"/api/webhooks/ado/{_connectorId}", content);

        // Should not return 401/403 — endpoint is AllowAnonymous.
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task WebhookEndpoint_Returns200_ForValidPayload()
    {
        var payload = BuildPayload("workitem.updated");
        var content = new StringContent(payload, Encoding.UTF8, "application/json");

        var response = await _client.PostAsync($"/api/webhooks/ado/{_connectorId}", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task WebhookEndpoint_Returns404_ForUnknownConnector()
    {
        var payload = BuildPayload("workitem.updated");
        var content = new StringContent(payload, Encoding.UTF8, "application/json");

        var response = await _client.PostAsync($"/api/webhooks/ado/{Guid.NewGuid()}", content);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task WebhookEndpoint_CreatesSyncRun_OnValidPayload()
    {
        var payload = BuildPayload("workitem.created", notificationId: 999);
        var content = new StringContent(payload, Encoding.UTF8, "application/json");

        await _client.PostAsync($"/api/webhooks/ado/{_connectorId}", content);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmartKbDbContext>();
        var syncRun = await db.SyncRuns.FirstOrDefaultAsync(s => s.ConnectorId == _connectorId);
        Assert.NotNull(syncRun);
        Assert.Equal(SyncRunStatus.Pending, syncRun.Status);
        Assert.False(syncRun.IsBackfill);
    }

    private static string BuildPayload(string eventType, int notificationId = 1) =>
        $$$"""{"eventType":"{{{eventType}}}","notificationId":{{{notificationId}}},"id":"evt-test","publisherId":"tfs","resource":{"id":42}}""";
}
