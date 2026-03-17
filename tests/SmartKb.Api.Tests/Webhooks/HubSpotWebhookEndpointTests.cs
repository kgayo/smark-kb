using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartKb.Api.Tests.Connectors;
using SmartKb.Contracts.Enums;
using SmartKb.Data;
using SmartKb.Data.Entities;

namespace SmartKb.Api.Tests.Webhooks;

public sealed class HubSpotWebhookEndpointTests : IClassFixture<ConnectorTestFactory>, IAsyncLifetime
{
    private readonly ConnectorTestFactory _factory;
    private HttpClient _client = null!;
    private Guid _connectorId;

    public HubSpotWebhookEndpointTests(ConnectorTestFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.EnsureDatabaseAsync();
        _client = _factory.CreateClient(); // Unauthenticated — webhook endpoint is anonymous.

        // Seed a HubSpot connector and webhook subscription.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmartKbDbContext>();

        _connectorId = Guid.NewGuid();
        db.Connectors.Add(new ConnectorEntity
        {
            Id = _connectorId,
            TenantId = "tenant-1",
            Name = $"hubspot-webhook-test-{Guid.NewGuid():N}",
            ConnectorType = ConnectorType.HubSpot,
            Status = ConnectorStatus.Enabled,
            AuthType = SecretAuthType.Pat,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        db.Set<WebhookSubscriptionEntity>().Add(new WebhookSubscriptionEntity
        {
            Id = Guid.NewGuid(),
            ConnectorId = _connectorId,
            TenantId = "tenant-1",
            ExternalSubscriptionId = "hubspot-sub-1",
            EventType = "ticket.propertyChange",
            CallbackUrl = $"https://test/api/webhooks/hubspot/{_connectorId}",
            WebhookSecretName = null, // Skip signature verification in this test.
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
        var payload = BuildPayload("ticket.propertyChange");
        var content = new StringContent(payload, Encoding.UTF8, "application/json");

        var response = await _client.PostAsync($"/api/webhooks/hubspot/{_connectorId}", content);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task WebhookEndpoint_Returns200_ForValidPayload()
    {
        var payload = BuildPayload("ticket.creation");
        var content = new StringContent(payload, Encoding.UTF8, "application/json");

        var response = await _client.PostAsync($"/api/webhooks/hubspot/{_connectorId}", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task WebhookEndpoint_Returns404_ForUnknownConnector()
    {
        var payload = BuildPayload("ticket.creation");
        var content = new StringContent(payload, Encoding.UTF8, "application/json");

        var response = await _client.PostAsync($"/api/webhooks/hubspot/{Guid.NewGuid()}", content);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task WebhookEndpoint_CreatesSyncRun_OnValidPayload()
    {
        var payload = BuildPayload("ticket.propertyChange", eventId: 98765);
        var content = new StringContent(payload, Encoding.UTF8, "application/json");

        await _client.PostAsync($"/api/webhooks/hubspot/{_connectorId}", content);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmartKbDbContext>();
        var syncRun = await db.SyncRuns
            .FirstOrDefaultAsync(s => s.ConnectorId == _connectorId);
        Assert.NotNull(syncRun);
        Assert.Equal(SyncRunStatus.Pending, syncRun.Status);
        Assert.False(syncRun.IsBackfill);
    }

    [Fact]
    public async Task WebhookEndpoint_Returns400_ForInvalidPayload()
    {
        var content = new StringContent("not valid json", Encoding.UTF8, "application/json");

        var response = await _client.PostAsync($"/api/webhooks/hubspot/{_connectorId}", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static string BuildPayload(string subscriptionType, long eventId = 12345) =>
        $$$"""[{"eventId":{{{eventId}}},"subscriptionId":100,"portalId":99,"appId":1,"occurredAt":1711000000000,"subscriptionType":"{{{subscriptionType}}}","attemptNumber":0,"objectId":42,"changeSource":"CRM"}]""";
}
