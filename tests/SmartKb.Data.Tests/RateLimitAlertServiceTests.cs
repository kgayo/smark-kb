using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Services;
using SmartKb.Data.Entities;
using SmartKb.Data.Repositories;

namespace SmartKb.Data.Tests;

public class RateLimitAlertServiceTests : IDisposable
{
    private readonly SmartKbDbContext _db;
    private readonly SloSettings _sloSettings;
    private readonly FakeTimeProvider _timeProvider;
    private readonly RateLimitAlertService _service;
    private readonly Guid _connectorId = Guid.NewGuid();

    public RateLimitAlertServiceTests()
    {
        _db = TestDbContextFactory.Create();
        _sloSettings = new SloSettings
        {
            RateLimitAlertThreshold = 3,
            RateLimitAlertWindowMinutes = 15,
        };
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 3, 19, 12, 0, 0, TimeSpan.Zero));

        _db.Tenants.Add(new TenantEntity
        {
            TenantId = "t1",
            DisplayName = "Test Tenant",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.Tenants.Add(new TenantEntity
        {
            TenantId = "t2",
            DisplayName = "Other Tenant",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        var connector = new ConnectorEntity
        {
            Id = _connectorId,
            TenantId = "t1",
            Name = "ADO Prod",
            ConnectorType = ConnectorType.AzureDevOps,
            Status = ConnectorStatus.Enabled,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.Connectors.Add(connector);
        _db.SaveChanges();

        _service = new RateLimitAlertService(
            _db,
            Options.Create(_sloSettings),
            _timeProvider,
            NullLogger<RateLimitAlertService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task RecordRateLimitEvent_PersistsEvent()
    {
        await _service.RecordRateLimitEventAsync("t1", _connectorId, "AzureDevOps");

        var events = _db.RateLimitEvents.ToList();
        Assert.Single(events);
        Assert.Equal("t1", events[0].TenantId);
        Assert.Equal(_connectorId, events[0].ConnectorId);
        Assert.Equal("AzureDevOps", events[0].ConnectorType);
    }

    [Fact]
    public async Task GetAlerts_ReturnsEmpty_WhenNoEvents()
    {
        var result = await _service.GetRateLimitAlertsAsync("t1");

        Assert.Equal(0, result.TotalAlertingConnectors);
        Assert.Empty(result.Alerts);
    }

    [Fact]
    public async Task GetAlerts_ReturnsEmpty_WhenBelowThreshold()
    {
        // Add 2 events (threshold is 3).
        SeedEvents("t1", _connectorId, "AzureDevOps", 2, minutesAgo: 5);

        var result = await _service.GetRateLimitAlertsAsync("t1");

        Assert.Equal(0, result.TotalAlertingConnectors);
        Assert.Empty(result.Alerts);
    }

    [Fact]
    public async Task GetAlerts_ReturnsAlert_WhenAtThreshold()
    {
        SeedEvents("t1", _connectorId, "AzureDevOps", 3, minutesAgo: 5);

        var result = await _service.GetRateLimitAlertsAsync("t1");

        Assert.Equal(1, result.TotalAlertingConnectors);
        Assert.Single(result.Alerts);
        var alert = result.Alerts[0];
        Assert.Equal(_connectorId, alert.ConnectorId);
        Assert.Equal("ADO Prod", alert.ConnectorName);
        Assert.Equal("AzureDevOps", alert.ConnectorType);
        Assert.Equal(3, alert.HitCount);
        Assert.Equal(3, alert.Threshold);
        Assert.Equal(15, alert.WindowMinutes);
    }

    [Fact]
    public async Task GetAlerts_IgnoresEventsOutsideWindow()
    {
        // 3 events from 20 minutes ago (outside 15-min window).
        SeedEvents("t1", _connectorId, "AzureDevOps", 3, minutesAgo: 20);

        var result = await _service.GetRateLimitAlertsAsync("t1");

        Assert.Equal(0, result.TotalAlertingConnectors);
        Assert.Empty(result.Alerts);
    }

    [Fact]
    public async Task GetAlerts_EnforcesTenantIsolation()
    {
        var otherConnectorId = Guid.NewGuid();
        _db.Connectors.Add(new ConnectorEntity
        {
            Id = otherConnectorId,
            TenantId = "t2",
            Name = "Other Connector",
            ConnectorType = ConnectorType.SharePoint,
            Status = ConnectorStatus.Enabled,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();

        SeedEvents("t2", otherConnectorId, "SharePoint", 5, minutesAgo: 3);

        var result = await _service.GetRateLimitAlertsAsync("t1");
        Assert.Equal(0, result.TotalAlertingConnectors);
    }

    [Fact]
    public async Task GetAlerts_ReturnsMultipleConnectors()
    {
        var connector2Id = Guid.NewGuid();
        _db.Connectors.Add(new ConnectorEntity
        {
            Id = connector2Id,
            TenantId = "t1",
            Name = "SharePoint Docs",
            ConnectorType = ConnectorType.SharePoint,
            Status = ConnectorStatus.Enabled,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();

        SeedEvents("t1", _connectorId, "AzureDevOps", 4, minutesAgo: 5);
        SeedEvents("t1", connector2Id, "SharePoint", 3, minutesAgo: 2);

        var result = await _service.GetRateLimitAlertsAsync("t1");

        Assert.Equal(2, result.TotalAlertingConnectors);
        Assert.Equal(2, result.Alerts.Count);
    }

    [Fact]
    public async Task GetAlerts_IncludesMostRecentHitTimestamp()
    {
        SeedEvents("t1", _connectorId, "AzureDevOps", 5, minutesAgo: 10);

        var result = await _service.GetRateLimitAlertsAsync("t1");

        Assert.Single(result.Alerts);
        Assert.NotNull(result.Alerts[0].MostRecentHit);
    }

    [Fact]
    public async Task RecordEvent_UsesTimeProvider()
    {
        await _service.RecordRateLimitEventAsync("t1", _connectorId, "AzureDevOps");

        var evt = _db.RateLimitEvents.Single();
        Assert.Equal(_timeProvider.GetUtcNow(), evt.OccurredAt);
    }

    [Fact]
    public async Task GetAlerts_ConnectorInDifferentTenant_FallsBackToUnknown()
    {
        // Connector exists but is in tenant t2, so name lookup for t1 won't find it.
        var crossTenantId = Guid.NewGuid();
        _db.Connectors.Add(new ConnectorEntity
        {
            Id = crossTenantId,
            TenantId = "t2",
            Name = "Cross Tenant Connector",
            ConnectorType = ConnectorType.HubSpot,
            Status = ConnectorStatus.Enabled,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();

        // Events are tagged with t1 but connector is in t2.
        SeedEvents("t1", crossTenantId, "HubSpot", 3, minutesAgo: 1);

        var result = await _service.GetRateLimitAlertsAsync("t1");

        Assert.Single(result.Alerts);
        Assert.Equal("Unknown", result.Alerts[0].ConnectorName);
    }

    private void SeedEvents(string tenantId, Guid connectorId, string connectorType, int count, int minutesAgo)
    {
        var baseTime = _timeProvider.GetUtcNow().AddMinutes(-minutesAgo);
        for (var i = 0; i < count; i++)
        {
            _db.RateLimitEvents.Add(new RateLimitEventEntity
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ConnectorId = connectorId,
                ConnectorType = connectorType,
                OccurredAt = baseTime.AddSeconds(i),
            });
        }
        _db.SaveChanges();
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;

        public FakeTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) => _now += duration;
    }
}
