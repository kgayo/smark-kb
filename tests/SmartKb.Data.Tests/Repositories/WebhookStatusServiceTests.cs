using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SmartKb.Contracts.Enums;
using SmartKb.Data.Entities;
using SmartKb.Data.Repositories;

namespace SmartKb.Data.Tests.Repositories;

public sealed class WebhookStatusServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SmartKbDbContext _db;
    private readonly WebhookStatusService _service;

    public WebhookStatusServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<SmartKbDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new SmartKbDbContext(options);
        _db.Database.EnsureCreated();

        _service = new WebhookStatusService(_db, NullLogger<WebhookStatusService>.Instance);

        SeedData();
    }

    private void SeedData()
    {
        var now = DateTimeOffset.UtcNow;
        _db.Tenants.Add(new TenantEntity { TenantId = "t1", DisplayName = "Test", CreatedAt = now });
        _db.Tenants.Add(new TenantEntity { TenantId = "t2", DisplayName = "Other", CreatedAt = now });

        var conn1 = new ConnectorEntity
        {
            Id = Guid.NewGuid(),
            TenantId = "t1",
            Name = "ADO Prod",
            ConnectorType = ConnectorType.AzureDevOps,
            Status = ConnectorStatus.Enabled,
            AuthType = SecretAuthType.Pat,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var conn2 = new ConnectorEntity
        {
            Id = Guid.NewGuid(),
            TenantId = "t1",
            Name = "SharePoint",
            ConnectorType = ConnectorType.SharePoint,
            Status = ConnectorStatus.Disabled,
            AuthType = SecretAuthType.OAuth,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var conn3 = new ConnectorEntity
        {
            Id = Guid.NewGuid(),
            TenantId = "t2",
            Name = "Other Tenant",
            ConnectorType = ConnectorType.HubSpot,
            Status = ConnectorStatus.Enabled,
            AuthType = SecretAuthType.Pat,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.Connectors.AddRange(conn1, conn2, conn3);

        _db.WebhookSubscriptions.AddRange(
            new WebhookSubscriptionEntity
            {
                Id = Guid.NewGuid(),
                ConnectorId = conn1.Id,
                TenantId = "t1",
                EventType = "workitem.created",
                CallbackUrl = "https://test/ado/1",
                IsActive = true,
                PollingFallbackActive = false,
                ConsecutiveFailures = 0,
                LastDeliveryAt = now.AddMinutes(-5),
                CreatedAt = now,
                UpdatedAt = now,
            },
            new WebhookSubscriptionEntity
            {
                Id = Guid.NewGuid(),
                ConnectorId = conn1.Id,
                TenantId = "t1",
                EventType = "workitem.updated",
                CallbackUrl = "https://test/ado/2",
                IsActive = true,
                PollingFallbackActive = true,
                ConsecutiveFailures = 3,
                LastDeliveryAt = now.AddHours(-2),
                NextPollAt = now.AddMinutes(5),
                CreatedAt = now,
                UpdatedAt = now,
            },
            new WebhookSubscriptionEntity
            {
                Id = Guid.NewGuid(),
                ConnectorId = conn2.Id,
                TenantId = "t1",
                EventType = "driveItem.updated",
                CallbackUrl = "https://test/sp/1",
                IsActive = false,
                PollingFallbackActive = false,
                ConsecutiveFailures = 0,
                CreatedAt = now,
                UpdatedAt = now,
            },
            new WebhookSubscriptionEntity
            {
                Id = Guid.NewGuid(),
                ConnectorId = conn3.Id,
                TenantId = "t2",
                EventType = "ticket.created",
                CallbackUrl = "https://test/hub/1",
                IsActive = true,
                PollingFallbackActive = false,
                ConsecutiveFailures = 0,
                CreatedAt = now,
                UpdatedAt = now,
            });

        _db.SyncRuns.Add(new SyncRunEntity
        {
            Id = Guid.NewGuid(),
            ConnectorId = conn1.Id,
            TenantId = "t1",
            Status = SyncRunStatus.Completed,
            IsBackfill = false,
            StartedAt = now.AddMinutes(-10),
            CompletedAt = now.AddMinutes(-5),
            RecordsProcessed = 42,
            RecordsFailed = 0,
        });

        _db.SaveChanges();

        // Store IDs for tests.
        _conn1Id = conn1.Id;
        _conn2Id = conn2.Id;
    }

    private Guid _conn1Id;
    private Guid _conn2Id;

    [Fact]
    public async Task GetByConnector_ReturnsWebhooksForSpecificConnector()
    {
        var result = await _service.GetByConnectorAsync("t1", _conn1Id);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(1, result.ActiveCount);
        Assert.Equal(1, result.FallbackCount);
        Assert.All(result.Subscriptions, s => Assert.Equal(_conn1Id, s.ConnectorId));
    }

    [Fact]
    public async Task GetByConnector_ReturnsEmptyForUnknownConnector()
    {
        var result = await _service.GetByConnectorAsync("t1", Guid.NewGuid());

        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Subscriptions);
    }

    [Fact]
    public async Task GetAll_ReturnsOnlyTenantWebhooks()
    {
        var result = await _service.GetAllAsync("t1");

        Assert.Equal(3, result.TotalCount);
        Assert.All(result.Subscriptions, s => Assert.Equal("t1", s.ConnectorName == "ADO Prod" || s.ConnectorName == "SharePoint" ? "t1" : "fail"));
    }

    [Fact]
    public async Task GetAll_TenantIsolation_DoesNotLeakCrossTenant()
    {
        var result = await _service.GetAllAsync("t2");

        Assert.Equal(1, result.TotalCount);
        Assert.All(result.Subscriptions, s => Assert.Equal("Other Tenant", s.ConnectorName));
    }

    [Fact]
    public async Task GetAll_CorrectlyCountsActiveAndFallback()
    {
        var result = await _service.GetAllAsync("t1");

        Assert.Equal(1, result.ActiveCount);
        Assert.Equal(1, result.FallbackCount);
    }

    [Fact]
    public async Task GetByConnector_MapsAllFields()
    {
        var result = await _service.GetByConnectorAsync("t1", _conn1Id);

        var healthy = result.Subscriptions.First(s => s.EventType == "workitem.created");
        Assert.True(healthy.IsActive);
        Assert.False(healthy.PollingFallbackActive);
        Assert.Equal(0, healthy.ConsecutiveFailures);
        Assert.NotNull(healthy.LastDeliveryAt);
        Assert.Equal("ADO Prod", healthy.ConnectorName);
        Assert.Equal("AzureDevOps", healthy.ConnectorType);

        var fallback = result.Subscriptions.First(s => s.EventType == "workitem.updated");
        Assert.True(fallback.PollingFallbackActive);
        Assert.Equal(3, fallback.ConsecutiveFailures);
        Assert.NotNull(fallback.NextPollAt);
    }

    [Fact]
    public async Task GetDiagnosticsSummary_ReturnsCorrectCounts()
    {
        var result = await _service.GetDiagnosticsSummaryAsync("t1");

        Assert.Equal(2, result.TotalConnectors);
        Assert.Equal(1, result.EnabledConnectors);
        Assert.Equal(1, result.DisabledConnectors);
        Assert.Equal(3, result.TotalWebhooks);
        Assert.Equal(1, result.ActiveWebhooks);
        Assert.Equal(1, result.FallbackWebhooks);
    }

    [Fact]
    public async Task GetDiagnosticsSummary_ConnectorHealth_IncludesSyncStatus()
    {
        var result = await _service.GetDiagnosticsSummaryAsync("t1");

        var adoHealth = result.ConnectorHealth.First(c => c.Name == "ADO Prod");
        Assert.Equal("Completed", adoHealth.LastSyncStatus);
        Assert.NotNull(adoHealth.LastSyncAt);
        Assert.Equal(2, adoHealth.WebhookCount);
        Assert.Equal(1, adoHealth.WebhooksInFallback);
        Assert.Equal(3, adoHealth.TotalFailures);
    }

    [Fact]
    public async Task GetDiagnosticsSummary_ConnectorWithNoSync_ShowsNullLastSync()
    {
        var result = await _service.GetDiagnosticsSummaryAsync("t1");

        var spHealth = result.ConnectorHealth.First(c => c.Name == "SharePoint");
        Assert.Null(spHealth.LastSyncStatus);
        Assert.Null(spHealth.LastSyncAt);
    }

    [Fact]
    public async Task GetDiagnosticsSummary_TenantIsolation()
    {
        var result = await _service.GetDiagnosticsSummaryAsync("t2");

        Assert.Equal(1, result.TotalConnectors);
        Assert.Single(result.ConnectorHealth);
        Assert.Equal("Other Tenant", result.ConnectorHealth[0].Name);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
