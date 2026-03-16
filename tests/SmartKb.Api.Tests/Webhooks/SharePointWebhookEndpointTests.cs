using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartKb.Api.Tests.Connectors;
using SmartKb.Contracts.Enums;
using SmartKb.Data;
using SmartKb.Data.Entities;

namespace SmartKb.Api.Tests.Webhooks;

public sealed class SharePointWebhookEndpointTests : IClassFixture<ConnectorTestFactory>, IAsyncLifetime
{
    private readonly ConnectorTestFactory _factory;
    private HttpClient _client = null!;
    private Guid _connectorId;

    public SharePointWebhookEndpointTests(ConnectorTestFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.EnsureDatabaseAsync();
        _client = _factory.CreateClient();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmartKbDbContext>();

        _connectorId = Guid.NewGuid();
        db.Connectors.Add(new ConnectorEntity
        {
            Id = _connectorId,
            TenantId = "tenant-1",
            Name = $"sp-webhook-test-{Guid.NewGuid():N}",
            ConnectorType = ConnectorType.SharePoint,
            Status = ConnectorStatus.Enabled,
            AuthType = SecretAuthType.OAuth,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        // Add subscription with no secret (skip clientState verification in this test).
        db.Set<WebhookSubscriptionEntity>().Add(new WebhookSubscriptionEntity
        {
            Id = Guid.NewGuid(),
            ConnectorId = _connectorId,
            TenantId = "tenant-1",
            ExternalSubscriptionId = "graph-sub-1",
            EventType = "driveItem.changed.drive-1",
            CallbackUrl = $"https://test/api/webhooks/msgraph/{_connectorId}",
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
        var payload = BuildNotificationPayload();
        var content = new StringContent(payload, Encoding.UTF8, "application/json");

        var response = await _client.PostAsync($"/api/webhooks/msgraph/{_connectorId}", content);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task WebhookEndpoint_Returns200_ForValidPayload()
    {
        var payload = BuildNotificationPayload();
        var content = new StringContent(payload, Encoding.UTF8, "application/json");

        var response = await _client.PostAsync($"/api/webhooks/msgraph/{_connectorId}", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task WebhookEndpoint_Returns404_ForUnknownConnector()
    {
        var payload = BuildNotificationPayload();
        var content = new StringContent(payload, Encoding.UTF8, "application/json");

        var response = await _client.PostAsync($"/api/webhooks/msgraph/{Guid.NewGuid()}", content);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task WebhookEndpoint_ValidationHandshake_ReturnsToken()
    {
        var response = await _client.PostAsync(
            $"/api/webhooks/msgraph/{_connectorId}?validationToken=test-validation-token-123",
            new StringContent("", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("test-validation-token-123", body);
    }

    [Fact]
    public async Task WebhookEndpoint_CreatesSyncRun_OnValidPayload()
    {
        var payload = BuildNotificationPayload();
        var content = new StringContent(payload, Encoding.UTF8, "application/json");

        await _client.PostAsync($"/api/webhooks/msgraph/{_connectorId}", content);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmartKbDbContext>();
        var syncRun = await db.SyncRuns.FirstOrDefaultAsync(s => s.ConnectorId == _connectorId);
        Assert.NotNull(syncRun);
        Assert.Equal(SyncRunStatus.Pending, syncRun.Status);
        Assert.False(syncRun.IsBackfill);
    }

    private static string BuildNotificationPayload(string clientState = "any-state") =>
        $$"""
        {
            "value": [{
                "subscriptionId": "graph-sub-1",
                "clientState": "{{clientState}}",
                "changeType": "updated",
                "resource": "/drives/drive-1/root",
                "resourceData": {"@odata.type": "#Microsoft.Graph.DriveItem", "id": "item-1"}
            }]
        }
        """;
}
