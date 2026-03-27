using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SmartKb.Api.Webhooks;
using SmartKb.Contracts.Configuration;
using SmartKb.Data;
using SmartKb.Data.Entities;

namespace SmartKb.Api.Tests.Webhooks;

public sealed class WebhookFailureHelperTests : IAsyncLifetime
{
    private SmartKbDbContext _db = null!;
    private WebhookSettings _settings = null!;
    private Guid _connectorId;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<SmartKbDbContext>()
            .UseInMemoryDatabase($"failure-helper-tests-{Guid.NewGuid()}")
            .Options;

        _db = new SmartKbDbContext(options);
        await _db.Database.EnsureCreatedAsync();

        _settings = new WebhookSettings
        {
            BaseCallbackUrl = "https://test.example.com",
            FailureThresholdForFallback = 3,
            PollingFallbackIntervalSeconds = 300,
            PollingJitterMaxSeconds = 60,
        };

        var tenant = new TenantEntity { TenantId = "t1", DisplayName = "Test", CreatedAt = DateTimeOffset.UtcNow };
        _db.Tenants.Add(tenant);

        var connector = new ConnectorEntity
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            TenantId = "t1",
            ConnectorType = SmartKb.Contracts.Enums.ConnectorType.AzureDevOps,
            Status = SmartKb.Contracts.Enums.ConnectorStatus.Enabled,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _connectorId = connector.Id;
        _db.Connectors.Add(connector);

        _db.Set<WebhookSubscriptionEntity>().Add(new WebhookSubscriptionEntity
        {
            Id = Guid.NewGuid(),
            ConnectorId = _connectorId,
            EventType = "workitem.created",
            ExternalSubscriptionId = "sub-1",
            WebhookSecretName = "secret-1",
            IsActive = true,
            ConsecutiveFailures = 0,
            PollingFallbackActive = false,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        await _db.SaveChangesAsync();
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task RecordDeliveryFailureAsync_IncrementsConsecutiveFailures()
    {
        await WebhookFailureHelper.RecordDeliveryFailureAsync(
            _db, _settings, _connectorId, "ADO", NullLogger.Instance);

        var sub = await _db.Set<WebhookSubscriptionEntity>()
            .FirstAsync(w => w.ConnectorId == _connectorId);

        Assert.Equal(1, sub.ConsecutiveFailures);
        Assert.False(sub.PollingFallbackActive);
    }

    [Fact]
    public async Task RecordDeliveryFailureAsync_ActivatesFallback_AfterThreshold()
    {
        await WebhookFailureHelper.RecordDeliveryFailureAsync(
            _db, _settings, _connectorId, "HubSpot", NullLogger.Instance);
        await WebhookFailureHelper.RecordDeliveryFailureAsync(
            _db, _settings, _connectorId, "HubSpot", NullLogger.Instance);
        await WebhookFailureHelper.RecordDeliveryFailureAsync(
            _db, _settings, _connectorId, "HubSpot", NullLogger.Instance);

        var sub = await _db.Set<WebhookSubscriptionEntity>()
            .FirstAsync(w => w.ConnectorId == _connectorId);

        Assert.Equal(3, sub.ConsecutiveFailures);
        Assert.True(sub.PollingFallbackActive);
        Assert.NotNull(sub.NextPollAt);
    }

    [Fact]
    public async Task RecordDeliveryFailureAsync_DoesNothing_WhenNoActiveSubscriptions()
    {
        var unknownConnectorId = Guid.NewGuid();

        await WebhookFailureHelper.RecordDeliveryFailureAsync(
            _db, _settings, unknownConnectorId, "ClickUp", NullLogger.Instance);

        var sub = await _db.Set<WebhookSubscriptionEntity>()
            .FirstAsync(w => w.ConnectorId == _connectorId);

        Assert.Equal(0, sub.ConsecutiveFailures);
    }

    [Fact]
    public void ComputeNextPollTime_AddsIntervalWithJitter()
    {
        var from = DateTimeOffset.UtcNow;
        var next = WebhookFailureHelper.ComputeNextPollTime(_settings, from);

        Assert.True(next >= from.AddSeconds(300));
        Assert.True(next <= from.AddSeconds(361));
    }

    [Fact]
    public async Task RecordDeliveryFailureAsync_IncludesConnectorTypeInLog()
    {
        var logger = new CapturingLogger();

        // Push past threshold so log fires.
        for (var i = 0; i < 3; i++)
            await WebhookFailureHelper.RecordDeliveryFailureAsync(
                _db, _settings, _connectorId, "SharePoint", logger);

        Assert.Contains(logger.Entries, e =>
            e.Level == Microsoft.Extensions.Logging.LogLevel.Warning &&
            e.Message.Contains("SharePoint"));
    }

    [Fact]
    public void RecordDeliverySuccess_ResetsFailureCounters()
    {
        var sub = new WebhookSubscriptionEntity
        {
            Id = Guid.NewGuid(),
            ConnectorId = _connectorId,
            EventType = "workitem.created",
            ExternalSubscriptionId = "sub-test",
            WebhookSecretName = "secret-test",
            IsActive = true,
            ConsecutiveFailures = 5,
            PollingFallbackActive = true,
            NextPollAt = DateTimeOffset.UtcNow.AddMinutes(10),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow.AddHours(-1),
        };

        WebhookFailureHelper.RecordDeliverySuccess([sub]);

        Assert.Equal(0, sub.ConsecutiveFailures);
        Assert.False(sub.PollingFallbackActive);
        Assert.Null(sub.NextPollAt);
        Assert.NotNull(sub.LastDeliveryAt);
        Assert.True(sub.UpdatedAt > DateTimeOffset.UtcNow.AddSeconds(-5));
    }

    [Fact]
    public void RecordDeliverySuccess_HandlesMultipleSubscriptions()
    {
        var subs = new[]
        {
            new WebhookSubscriptionEntity
            {
                Id = Guid.NewGuid(), ConnectorId = _connectorId, EventType = "a",
                ExternalSubscriptionId = "s1", WebhookSecretName = "k1", IsActive = true,
                ConsecutiveFailures = 3, PollingFallbackActive = true,
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            },
            new WebhookSubscriptionEntity
            {
                Id = Guid.NewGuid(), ConnectorId = _connectorId, EventType = "b",
                ExternalSubscriptionId = "s2", WebhookSecretName = "k2", IsActive = true,
                ConsecutiveFailures = 1, PollingFallbackActive = false,
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            },
        };

        WebhookFailureHelper.RecordDeliverySuccess(subs);

        Assert.All(subs, s =>
        {
            Assert.Equal(0, s.ConsecutiveFailures);
            Assert.False(s.PollingFallbackActive);
            Assert.Null(s.NextPollAt);
            Assert.NotNull(s.LastDeliveryAt);
        });
    }

    private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger
    {
        public List<(Microsoft.Extensions.Logging.LogLevel Level, string Message)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
