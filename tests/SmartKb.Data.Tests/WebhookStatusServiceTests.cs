using Microsoft.Extensions.Logging.Abstractions;
using SmartKb.Contracts.Enums;
using SmartKb.Data.Entities;
using SmartKb.Data.Repositories;

namespace SmartKb.Data.Tests;

public sealed class WebhookStatusServiceTests : IDisposable
{
    private readonly SmartKbDbContext _db;
    private readonly WebhookStatusService _service;

    private const string TenantId = "t1";
    private const string OtherTenantId = "t2";

    public WebhookStatusServiceTests()
    {
        _db = TestDbContextFactory.Create();

        _db.Tenants.Add(new TenantEntity { TenantId = TenantId, DisplayName = "Test Tenant", IsActive = true, CreatedAt = DateTimeOffset.UtcNow });
        _db.Tenants.Add(new TenantEntity { TenantId = OtherTenantId, DisplayName = "Other Tenant", IsActive = true, CreatedAt = DateTimeOffset.UtcNow });
        _db.SaveChanges();

        _service = new WebhookStatusService(_db, NullLogger<WebhookStatusService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private ConnectorEntity SeedConnector(string tenantId, string name, ConnectorType type = ConnectorType.AzureDevOps, ConnectorStatus status = ConnectorStatus.Enabled)
    {
        var connector = new ConnectorEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            ConnectorType = type,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.Connectors.Add(connector);
        _db.SaveChanges();
        return connector;
    }

    private WebhookSubscriptionEntity SeedWebhook(ConnectorEntity connector, string eventType, bool isActive = true, bool pollingFallback = false, int failures = 0)
    {
        var webhook = new WebhookSubscriptionEntity
        {
            Id = Guid.NewGuid(),
            ConnectorId = connector.Id,
            TenantId = connector.TenantId,
            EventType = eventType,
            CallbackUrl = $"https://example.com/webhooks/{connector.Id}",
            IsActive = isActive,
            PollingFallbackActive = pollingFallback,
            ConsecutiveFailures = failures,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.WebhookSubscriptions.Add(webhook);
        _db.SaveChanges();
        return webhook;
    }

    private SyncRunEntity SeedSyncRun(ConnectorEntity connector, SyncRunStatus status, DateTimeOffset startedAt, DateTimeOffset? completedAt = null)
    {
        var run = new SyncRunEntity
        {
            Id = Guid.NewGuid(),
            ConnectorId = connector.Id,
            TenantId = connector.TenantId,
            Status = status,
            StartedAt = startedAt,
            CompletedAt = completedAt,
        };
        _db.SyncRuns.Add(run);
        _db.SaveChanges();
        return run;
    }

    // --- GetByConnectorAsync ---

    [Fact]
    public async Task GetByConnector_ReturnsEmpty_WhenNoWebhooks()
    {
        var connector = SeedConnector(TenantId, "Empty Connector");

        var result = await _service.GetByConnectorAsync(TenantId, connector.Id);

        Assert.Empty(result.Subscriptions);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.ActiveCount);
        Assert.Equal(0, result.FallbackCount);
    }

    [Fact]
    public async Task GetByConnector_ReturnsMatchingWebhooks_OrderedByEventType()
    {
        var connector = SeedConnector(TenantId, "ADO Prod");
        SeedWebhook(connector, "workitem.updated", isActive: true);
        SeedWebhook(connector, "workitem.created", isActive: true);

        var result = await _service.GetByConnectorAsync(TenantId, connector.Id);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.ActiveCount);
        Assert.Equal(0, result.FallbackCount);
        Assert.Equal("workitem.created", result.Subscriptions[0].EventType);
        Assert.Equal("workitem.updated", result.Subscriptions[1].EventType);
    }

    [Fact]
    public async Task GetByConnector_ExcludesOtherConnectors()
    {
        var c1 = SeedConnector(TenantId, "Connector A");
        var c2 = SeedConnector(TenantId, "Connector B");
        SeedWebhook(c1, "workitem.created");
        SeedWebhook(c2, "workitem.created");

        var result = await _service.GetByConnectorAsync(TenantId, c1.Id);

        Assert.Single(result.Subscriptions);
        Assert.Equal(c1.Id, result.Subscriptions[0].ConnectorId);
    }

    [Fact]
    public async Task GetByConnector_TenantIsolation_ReturnsEmpty()
    {
        var connector = SeedConnector(OtherTenantId, "Other Tenant Connector");
        SeedWebhook(connector, "workitem.created");

        var result = await _service.GetByConnectorAsync(TenantId, connector.Id);

        Assert.Empty(result.Subscriptions);
    }

    [Fact]
    public async Task GetByConnector_CountsActiveAndFallback()
    {
        var connector = SeedConnector(TenantId, "Mixed Connector");
        SeedWebhook(connector, "workitem.created", isActive: true, pollingFallback: false);
        SeedWebhook(connector, "workitem.updated", isActive: true, pollingFallback: true, failures: 3);
        SeedWebhook(connector, "workitem.deleted", isActive: false, pollingFallback: false);

        var result = await _service.GetByConnectorAsync(TenantId, connector.Id);

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(1, result.ActiveCount);  // active + not fallback
        Assert.Equal(1, result.FallbackCount);
    }

    // --- GetAllAsync ---

    [Fact]
    public async Task GetAll_ReturnsEmpty_WhenNoWebhooks()
    {
        var result = await _service.GetAllAsync(TenantId);

        Assert.Empty(result.Subscriptions);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task GetAll_ReturnsAllWebhooksForTenant_OrderedByConnectorNameThenEventType()
    {
        var c1 = SeedConnector(TenantId, "Beta Connector");
        var c2 = SeedConnector(TenantId, "Alpha Connector");
        SeedWebhook(c1, "workitem.updated");
        SeedWebhook(c1, "workitem.created");
        SeedWebhook(c2, "ticket.created");

        var result = await _service.GetAllAsync(TenantId);

        Assert.Equal(3, result.TotalCount);
        // Alpha connector first, then Beta
        Assert.Equal("Alpha Connector", result.Subscriptions[0].ConnectorName);
        Assert.Equal("Beta Connector", result.Subscriptions[1].ConnectorName);
        Assert.Equal("workitem.created", result.Subscriptions[1].EventType);
        Assert.Equal("workitem.updated", result.Subscriptions[2].EventType);
    }

    [Fact]
    public async Task GetAll_TenantIsolation_ExcludesOtherTenants()
    {
        var c1 = SeedConnector(TenantId, "My Connector");
        var c2 = SeedConnector(OtherTenantId, "Their Connector");
        SeedWebhook(c1, "workitem.created");
        SeedWebhook(c2, "workitem.created");

        var result = await _service.GetAllAsync(TenantId);

        Assert.Single(result.Subscriptions);
        Assert.Equal("My Connector", result.Subscriptions[0].ConnectorName);
    }

    // --- GetDiagnosticsSummaryAsync ---

    [Fact]
    public async Task GetDiagnosticsSummary_ReturnsZeros_WhenNoConnectors()
    {
        var result = await _service.GetDiagnosticsSummaryAsync(TenantId);

        Assert.Equal(0, result.TotalConnectors);
        Assert.Equal(0, result.EnabledConnectors);
        Assert.Equal(0, result.DisabledConnectors);
        Assert.Equal(0, result.TotalWebhooks);
        Assert.Equal(0, result.ActiveWebhooks);
        Assert.Equal(0, result.FallbackWebhooks);
        Assert.Equal(0, result.FailingWebhooks);
        Assert.Empty(result.ConnectorHealth);
    }

    [Fact]
    public async Task GetDiagnosticsSummary_CountsEnabledAndDisabled()
    {
        SeedConnector(TenantId, "Enabled 1", status: ConnectorStatus.Enabled);
        SeedConnector(TenantId, "Enabled 2", status: ConnectorStatus.Enabled);
        SeedConnector(TenantId, "Disabled 1", status: ConnectorStatus.Disabled);

        var result = await _service.GetDiagnosticsSummaryAsync(TenantId);

        Assert.Equal(3, result.TotalConnectors);
        Assert.Equal(2, result.EnabledConnectors);
        Assert.Equal(1, result.DisabledConnectors);
    }

    [Fact]
    public async Task GetDiagnosticsSummary_CountsWebhookStates()
    {
        var connector = SeedConnector(TenantId, "ADO Prod");
        SeedWebhook(connector, "workitem.created", isActive: true, pollingFallback: false, failures: 0);
        SeedWebhook(connector, "workitem.updated", isActive: true, pollingFallback: true, failures: 3);
        SeedWebhook(connector, "workitem.deleted", isActive: false, pollingFallback: false, failures: 1);

        var result = await _service.GetDiagnosticsSummaryAsync(TenantId);

        Assert.Equal(3, result.TotalWebhooks);
        Assert.Equal(1, result.ActiveWebhooks);   // active + not fallback
        Assert.Equal(1, result.FallbackWebhooks);
        Assert.Equal(2, result.FailingWebhooks);   // consecutiveFailures > 0
    }

    [Fact]
    public async Task GetDiagnosticsSummary_ConnectorHealth_IncludesLastSync()
    {
        var connector = SeedConnector(TenantId, "SP Prod", ConnectorType.SharePoint);
        var now = DateTimeOffset.UtcNow;
        SeedSyncRun(connector, SyncRunStatus.Completed, now.AddHours(-2), now.AddHours(-1));
        SeedSyncRun(connector, SyncRunStatus.Completed, now.AddMinutes(-30), now.AddMinutes(-25));
        SeedWebhook(connector, "change.notification", isActive: true, pollingFallback: true, failures: 2);

        var result = await _service.GetDiagnosticsSummaryAsync(TenantId);

        Assert.Single(result.ConnectorHealth);
        var health = result.ConnectorHealth[0];
        Assert.Equal(connector.Id, health.ConnectorId);
        Assert.Equal("SP Prod", health.Name);
        Assert.Equal("SharePoint", health.ConnectorType);
        Assert.Equal("Enabled", health.Status);
        Assert.Equal("Completed", health.LastSyncStatus);
        Assert.NotNull(health.LastSyncAt);
        Assert.Equal(1, health.WebhookCount);
        Assert.Equal(1, health.WebhooksInFallback);
        Assert.Equal(2, health.TotalFailures);
    }

    [Fact]
    public async Task GetDiagnosticsSummary_ConnectorHealth_NullLastSync_WhenNoSyncRuns()
    {
        SeedConnector(TenantId, "New Connector");

        var result = await _service.GetDiagnosticsSummaryAsync(TenantId);

        Assert.Single(result.ConnectorHealth);
        var health = result.ConnectorHealth[0];
        Assert.Null(health.LastSyncStatus);
        Assert.Null(health.LastSyncAt);
    }

    [Fact]
    public async Task GetDiagnosticsSummary_TenantIsolation_ExcludesOtherTenants()
    {
        SeedConnector(TenantId, "My Connector");
        SeedConnector(OtherTenantId, "Their Connector");

        var result = await _service.GetDiagnosticsSummaryAsync(TenantId);

        Assert.Equal(1, result.TotalConnectors);
        Assert.Single(result.ConnectorHealth);
        Assert.Equal("My Connector", result.ConnectorHealth[0].Name);
    }

    [Fact]
    public async Task GetDiagnosticsSummary_ServiceFlags_DefaultToFalse()
    {
        var result = await _service.GetDiagnosticsSummaryAsync(TenantId);

        Assert.False(result.ServiceBusConfigured);
        Assert.False(result.KeyVaultConfigured);
        Assert.False(result.OpenAiConfigured);
        Assert.False(result.SearchServiceConfigured);
    }

    [Fact]
    public async Task GetByConnector_MapsAllFields()
    {
        var connector = SeedConnector(TenantId, "Full Webhook");
        var webhook = SeedWebhook(connector, "workitem.created", isActive: true, pollingFallback: true, failures: 5);
        webhook.ExternalSubscriptionId = "ext-sub-123";
        webhook.LastDeliveryAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        webhook.NextPollAt = DateTimeOffset.UtcNow.AddMinutes(5);
        _db.SaveChanges();

        var result = await _service.GetByConnectorAsync(TenantId, connector.Id);

        Assert.Single(result.Subscriptions);
        var status = result.Subscriptions[0];
        Assert.Equal(webhook.Id, status.Id);
        Assert.Equal(connector.Id, status.ConnectorId);
        Assert.Equal("Full Webhook", status.ConnectorName);
        Assert.Equal("AzureDevOps", status.ConnectorType);
        Assert.Equal("workitem.created", status.EventType);
        Assert.True(status.IsActive);
        Assert.True(status.PollingFallbackActive);
        Assert.Equal(5, status.ConsecutiveFailures);
        Assert.NotNull(status.LastDeliveryAt);
        Assert.NotNull(status.NextPollAt);
        Assert.Equal("ext-sub-123", status.ExternalSubscriptionId);
    }
}
