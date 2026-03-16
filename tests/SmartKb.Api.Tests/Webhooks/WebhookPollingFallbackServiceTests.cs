using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SmartKb.Api.Webhooks;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data;
using SmartKb.Data.Entities;

namespace SmartKb.Api.Tests.Webhooks;

public sealed class WebhookPollingFallbackServiceTests : IAsyncLifetime
{
    private ServiceProvider _serviceProvider = null!;
    private SqliteConnection _connection = null!;
    private WebhookSettings _settings = null!;
    private WebhookPollingFallbackService _service = null!;
    private Guid _connectorId;

    public async Task InitializeAsync()
    {
        _settings = new WebhookSettings
        {
            PollingFallbackIntervalSeconds = 300,
            PollingJitterMaxSeconds = 60,
        };

        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddDbContext<SmartKbDbContext>(o => o.UseSqlite(_connection));
        services.AddSingleton<ISyncJobPublisher, TestPublisher>();
        services.AddSingleton<IAuditEventWriter, TestAuditWriter>();
        services.AddSingleton(_settings);

        _serviceProvider = services.BuildServiceProvider();

        // Create schema and seed data.
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmartKbDbContext>();
        await db.Database.EnsureCreatedAsync();

        db.Tenants.Add(new TenantEntity { TenantId = "t1", DisplayName = "Test", CreatedAt = DateTimeOffset.UtcNow });

        _connectorId = Guid.NewGuid();
        db.Connectors.Add(new ConnectorEntity
        {
            Id = _connectorId,
            TenantId = "t1",
            Name = "poll-test",
            ConnectorType = ConnectorType.AzureDevOps,
            Status = ConnectorStatus.Enabled,
            AuthType = SecretAuthType.Pat,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync();

        _service = new WebhookPollingFallbackService(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            _settings,
            NullLogger<WebhookPollingFallbackService>.Instance);
    }

    public async Task DisposeAsync()
    {
        _serviceProvider.Dispose();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task ProcessDuePolls_DoesNothing_WhenNoFallbackSubscriptions()
    {
        await _service.ProcessDuePollsAsync(CancellationToken.None);

        var publisher = _serviceProvider.GetRequiredService<ISyncJobPublisher>() as TestPublisher;
        Assert.Empty(publisher!.Messages);
    }

    [Fact]
    public async Task ProcessDuePolls_TriggersSyncAndReschedules_WhenDue()
    {
        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SmartKbDbContext>();
            db.Set<WebhookSubscriptionEntity>().Add(new WebhookSubscriptionEntity
            {
                Id = Guid.NewGuid(),
                ConnectorId = _connectorId,
                TenantId = "t1",
                EventType = "workitem.updated",
                CallbackUrl = "https://test/webhook",
                IsActive = true,
                PollingFallbackActive = true,
                ConsecutiveFailures = 5,
                NextPollAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await _service.ProcessDuePollsAsync(CancellationToken.None);

        var publisher = _serviceProvider.GetRequiredService<ISyncJobPublisher>() as TestPublisher;
        Assert.Single(publisher!.Messages);
        Assert.Equal(_connectorId, publisher.Messages[0].ConnectorId);
        Assert.False(publisher.Messages[0].IsBackfill);

        // Verify next poll was rescheduled.
        using var scope2 = _serviceProvider.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<SmartKbDbContext>();
        var sub = await db2.Set<WebhookSubscriptionEntity>().FirstAsync();
        Assert.NotNull(sub.NextPollAt);
        Assert.True(sub.NextPollAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task ProcessDuePolls_SkipsNotYetDue()
    {
        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SmartKbDbContext>();
            db.Set<WebhookSubscriptionEntity>().Add(new WebhookSubscriptionEntity
            {
                Id = Guid.NewGuid(),
                ConnectorId = _connectorId,
                TenantId = "t1",
                EventType = "workitem.updated",
                CallbackUrl = "https://test/webhook",
                IsActive = true,
                PollingFallbackActive = true,
                ConsecutiveFailures = 5,
                NextPollAt = DateTimeOffset.UtcNow.AddMinutes(10),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await _service.ProcessDuePollsAsync(CancellationToken.None);

        var publisher = _serviceProvider.GetRequiredService<ISyncJobPublisher>() as TestPublisher;
        Assert.Empty(publisher!.Messages);
    }

    [Fact]
    public async Task ProcessDuePolls_SkipsDisabledConnectors()
    {
        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SmartKbDbContext>();
            var connector = await db.Connectors.FindAsync(_connectorId);
            connector!.Status = ConnectorStatus.Disabled;
            db.Set<WebhookSubscriptionEntity>().Add(new WebhookSubscriptionEntity
            {
                Id = Guid.NewGuid(),
                ConnectorId = _connectorId,
                TenantId = "t1",
                EventType = "workitem.updated",
                CallbackUrl = "https://test/webhook",
                IsActive = true,
                PollingFallbackActive = true,
                ConsecutiveFailures = 5,
                NextPollAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await _service.ProcessDuePollsAsync(CancellationToken.None);

        var publisher = _serviceProvider.GetRequiredService<ISyncJobPublisher>() as TestPublisher;
        Assert.Empty(publisher!.Messages);
    }

    // --- Test doubles ---

    private sealed class TestPublisher : ISyncJobPublisher
    {
        public List<SyncJobMessage> Messages { get; } = [];
        public Task PublishAsync(SyncJobMessage message, CancellationToken ct = default)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class TestAuditWriter : IAuditEventWriter
    {
        public Task WriteAsync(AuditEvent auditEvent, CancellationToken ct = default) => Task.CompletedTask;
    }
}
