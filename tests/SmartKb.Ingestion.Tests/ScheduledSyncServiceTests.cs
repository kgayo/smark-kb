using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data;
using SmartKb.Data.Entities;
using SmartKb.Ingestion;

namespace SmartKb.Ingestion.Tests;

public class ScheduledSyncServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _sp;
    private readonly SmartKbDbContext _db;
    private readonly FakeSyncJobPublisher _publisher;
    private readonly ScheduledSyncSettings _settings;

    public ScheduledSyncServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _publisher = new FakeSyncJobPublisher();
        _settings = new ScheduledSyncSettings { Enabled = true, EvaluationIntervalSeconds = 60 };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<SmartKbDbContext>(o => o.UseSqlite(_connection));
        services.AddSingleton<ISyncJobPublisher>(_publisher);
        services.AddSingleton(_settings);

        _sp = services.BuildServiceProvider();
        _db = _sp.GetRequiredService<SmartKbDbContext>();
        _db.Database.EnsureCreated();

        SeedTenant("tenant-1");
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        _sp.Dispose();
    }

    // --- IsDue tests ---

    [Fact]
    public void IsDue_ReturnsFalse_WhenCronNotYetDue()
    {
        var now = new DateTimeOffset(2026, 3, 18, 10, 5, 0, TimeSpan.Zero);
        var connector = MakeConnector("0 * * * *", lastScheduledSyncAt: now.AddMinutes(-5));

        Assert.False(ScheduledSyncService.IsDue(connector, now));
    }

    [Fact]
    public void IsDue_ReturnsTrue_WhenCronIsDue()
    {
        var connector = MakeConnector("0 * * * *",
            lastScheduledSyncAt: new DateTimeOffset(2026, 3, 18, 9, 0, 0, TimeSpan.Zero));
        var now = new DateTimeOffset(2026, 3, 18, 10, 1, 0, TimeSpan.Zero);

        Assert.True(ScheduledSyncService.IsDue(connector, now));
    }

    [Fact]
    public void IsDue_ReturnsFalse_ForInvalidCronExpression()
    {
        var connector = MakeConnector("not-a-cron");
        var now = DateTimeOffset.UtcNow;

        Assert.False(ScheduledSyncService.IsDue(connector, now));
    }

    [Fact]
    public void IsDue_UsesLastCompletedSyncRun_WhenLastScheduledSyncAtIsNull()
    {
        var connector = MakeConnector("*/30 * * * *", lastScheduledSyncAt: null);
        connector.SyncRuns = new List<SyncRunEntity>
        {
            new()
            {
                Id = Guid.NewGuid(), ConnectorId = connector.Id, TenantId = connector.TenantId,
                Status = SyncRunStatus.Completed, IsBackfill = false,
                StartedAt = new DateTimeOffset(2026, 3, 18, 9, 15, 0, TimeSpan.Zero),
                CompletedAt = new DateTimeOffset(2026, 3, 18, 9, 20, 0, TimeSpan.Zero),
            },
        };
        var now = new DateTimeOffset(2026, 3, 18, 9, 55, 0, TimeSpan.Zero);

        Assert.True(ScheduledSyncService.IsDue(connector, now));
    }

    [Fact]
    public void IsDue_UsesCreatedAt_WhenNoSyncHistory()
    {
        var connector = MakeConnector("*/5 * * * *", lastScheduledSyncAt: null);
        connector.CreatedAt = new DateTimeOffset(2026, 3, 18, 9, 0, 0, TimeSpan.Zero);
        connector.SyncRuns = new List<SyncRunEntity>();

        var now = new DateTimeOffset(2026, 3, 18, 9, 6, 0, TimeSpan.Zero);

        Assert.True(ScheduledSyncService.IsDue(connector, now));
    }

    [Fact]
    public void IsDue_ReturnsFalse_WhenJustTriggered()
    {
        var connector = MakeConnector("*/5 * * * *",
            lastScheduledSyncAt: new DateTimeOffset(2026, 3, 18, 9, 5, 0, TimeSpan.Zero));
        var now = new DateTimeOffset(2026, 3, 18, 9, 6, 0, TimeSpan.Zero);

        Assert.False(ScheduledSyncService.IsDue(connector, now));
    }

    // --- EvaluateSchedulesAsync tests ---

    [Fact]
    public async Task EvaluateSchedules_TriggersSync_WhenDue()
    {
        var connector = AddConnector("0 * * * *", ConnectorStatus.Enabled,
            lastScheduledSyncAt: new DateTimeOffset(2026, 3, 18, 9, 0, 0, TimeSpan.Zero));

        var now = new DateTimeOffset(2026, 3, 18, 10, 1, 0, TimeSpan.Zero);
        var service = CreateService(now);

        await service.EvaluateSchedulesAsync(CancellationToken.None);

        Assert.Single(_publisher.Published);
        var msg = _publisher.Published[0];
        Assert.Equal(connector.Id, msg.ConnectorId);
        Assert.Equal(connector.TenantId, msg.TenantId);
        Assert.False(msg.IsBackfill);

        var syncRun = await _db.SyncRuns.FirstOrDefaultAsync(s => s.ConnectorId == connector.Id);
        Assert.NotNull(syncRun);
        Assert.Equal(SyncRunStatus.Pending, syncRun.Status);
        Assert.StartsWith("scheduled-", syncRun.IdempotencyKey);
    }

    [Fact]
    public async Task EvaluateSchedules_SkipsDisabledConnectors()
    {
        AddConnector("0 * * * *", ConnectorStatus.Disabled,
            lastScheduledSyncAt: new DateTimeOffset(2026, 3, 18, 9, 0, 0, TimeSpan.Zero));

        var now = new DateTimeOffset(2026, 3, 18, 10, 1, 0, TimeSpan.Zero);
        var service = CreateService(now);

        await service.EvaluateSchedulesAsync(CancellationToken.None);

        Assert.Empty(_publisher.Published);
    }

    [Fact]
    public async Task EvaluateSchedules_SkipsConnectorsWithNullCron()
    {
        AddConnector(null, ConnectorStatus.Enabled);

        var now = new DateTimeOffset(2026, 3, 18, 10, 1, 0, TimeSpan.Zero);
        var service = CreateService(now);

        await service.EvaluateSchedulesAsync(CancellationToken.None);

        Assert.Empty(_publisher.Published);
    }

    [Fact]
    public async Task EvaluateSchedules_SkipsConnectorsNotYetDue()
    {
        AddConnector("0 * * * *", ConnectorStatus.Enabled,
            lastScheduledSyncAt: new DateTimeOffset(2026, 3, 18, 10, 0, 0, TimeSpan.Zero));

        var now = new DateTimeOffset(2026, 3, 18, 10, 5, 0, TimeSpan.Zero);
        var service = CreateService(now);

        await service.EvaluateSchedulesAsync(CancellationToken.None);

        Assert.Empty(_publisher.Published);
    }

    [Fact]
    public async Task EvaluateSchedules_AvoidsDuplicateTriggers_ViaIdempotencyKey()
    {
        var connector = AddConnector("0 * * * *", ConnectorStatus.Enabled,
            lastScheduledSyncAt: new DateTimeOffset(2026, 3, 18, 9, 0, 0, TimeSpan.Zero));

        var now = new DateTimeOffset(2026, 3, 18, 10, 1, 0, TimeSpan.Zero);

        _db.SyncRuns.Add(new SyncRunEntity
        {
            Id = Guid.NewGuid(),
            ConnectorId = connector.Id,
            TenantId = connector.TenantId,
            Status = SyncRunStatus.Pending,
            IsBackfill = false,
            StartedAt = now,
            IdempotencyKey = $"scheduled-{connector.Id:N}-{now:yyyyMMddHHmm}",
        });
        await _db.SaveChangesAsync();

        var service = CreateService(now);
        await service.EvaluateSchedulesAsync(CancellationToken.None);

        Assert.Empty(_publisher.Published);
    }

    [Fact]
    public async Task EvaluateSchedules_UpdatesLastScheduledSyncAt()
    {
        var connector = AddConnector("0 * * * *", ConnectorStatus.Enabled,
            lastScheduledSyncAt: new DateTimeOffset(2026, 3, 18, 9, 0, 0, TimeSpan.Zero));

        var now = new DateTimeOffset(2026, 3, 18, 10, 1, 0, TimeSpan.Zero);
        var service = CreateService(now);

        await service.EvaluateSchedulesAsync(CancellationToken.None);

        await _db.Entry(connector).ReloadAsync();
        Assert.Equal(now, connector.LastScheduledSyncAt);
    }

    [Fact]
    public async Task EvaluateSchedules_IncludesCheckpoint_FromLastCompletedRun()
    {
        var connector = AddConnector("0 * * * *", ConnectorStatus.Enabled,
            lastScheduledSyncAt: new DateTimeOffset(2026, 3, 18, 9, 0, 0, TimeSpan.Zero));

        _db.SyncRuns.Add(new SyncRunEntity
        {
            Id = Guid.NewGuid(),
            ConnectorId = connector.Id,
            TenantId = connector.TenantId,
            Status = SyncRunStatus.Completed,
            IsBackfill = false,
            StartedAt = new DateTimeOffset(2026, 3, 18, 8, 0, 0, TimeSpan.Zero),
            CompletedAt = new DateTimeOffset(2026, 3, 18, 8, 5, 0, TimeSpan.Zero),
            Checkpoint = "checkpoint-abc",
        });
        await _db.SaveChangesAsync();

        var now = new DateTimeOffset(2026, 3, 18, 10, 1, 0, TimeSpan.Zero);
        var service = CreateService(now);

        await service.EvaluateSchedulesAsync(CancellationToken.None);

        Assert.Single(_publisher.Published);
        Assert.Equal("checkpoint-abc", _publisher.Published[0].Checkpoint);
    }

    [Fact]
    public async Task EvaluateSchedules_HandlesMultipleConnectors()
    {
        AddConnector("0 * * * *", ConnectorStatus.Enabled,
            lastScheduledSyncAt: new DateTimeOffset(2026, 3, 18, 9, 0, 0, TimeSpan.Zero));
        AddConnector("*/30 * * * *", ConnectorStatus.Enabled,
            lastScheduledSyncAt: new DateTimeOffset(2026, 3, 18, 9, 0, 0, TimeSpan.Zero));
        AddConnector("0 12 * * *", ConnectorStatus.Enabled,
            lastScheduledSyncAt: new DateTimeOffset(2026, 3, 18, 9, 0, 0, TimeSpan.Zero));

        var now = new DateTimeOffset(2026, 3, 18, 10, 1, 0, TimeSpan.Zero);
        var service = CreateService(now);

        await service.EvaluateSchedulesAsync(CancellationToken.None);

        Assert.Equal(2, _publisher.Published.Count);
    }

    [Fact]
    public async Task EvaluateSchedules_SkipsSoftDeletedConnectors()
    {
        var connector = AddConnector("0 * * * *", ConnectorStatus.Enabled,
            lastScheduledSyncAt: new DateTimeOffset(2026, 3, 18, 9, 0, 0, TimeSpan.Zero));
        connector.DeletedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();

        var now = new DateTimeOffset(2026, 3, 18, 10, 1, 0, TimeSpan.Zero);
        var service = CreateService(now);

        await service.EvaluateSchedulesAsync(CancellationToken.None);

        Assert.Empty(_publisher.Published);
    }

    [Fact]
    public async Task EvaluateSchedules_ContinuesOnSingleConnectorFailure()
    {
        AddConnector("bad-cron", ConnectorStatus.Enabled,
            lastScheduledSyncAt: new DateTimeOffset(2026, 3, 18, 9, 0, 0, TimeSpan.Zero));
        AddConnector("0 * * * *", ConnectorStatus.Enabled,
            lastScheduledSyncAt: new DateTimeOffset(2026, 3, 18, 9, 0, 0, TimeSpan.Zero));

        var now = new DateTimeOffset(2026, 3, 18, 10, 1, 0, TimeSpan.Zero);
        var service = CreateService(now);

        await service.EvaluateSchedulesAsync(CancellationToken.None);

        Assert.Single(_publisher.Published);
    }

    [Fact]
    public async Task EvaluateSchedules_PopulatesMessageFields_Correctly()
    {
        var connector = AddConnector("0 * * * *", ConnectorStatus.Enabled,
            lastScheduledSyncAt: new DateTimeOffset(2026, 3, 18, 9, 0, 0, TimeSpan.Zero));
        connector.SourceConfig = "{\"project\":\"test\"}";
        connector.FieldMapping = "[{\"source\":\"title\",\"target\":\"Title\"}]";
        connector.KeyVaultSecretName = "kv-secret-1";
        await _db.SaveChangesAsync();

        var now = new DateTimeOffset(2026, 3, 18, 10, 1, 0, TimeSpan.Zero);
        var service = CreateService(now);

        await service.EvaluateSchedulesAsync(CancellationToken.None);

        Assert.Single(_publisher.Published);
        var msg = _publisher.Published[0];
        Assert.Equal(connector.ConnectorType, msg.ConnectorType);
        Assert.Equal(connector.SourceConfig, msg.SourceConfig);
        Assert.Equal(connector.FieldMapping, msg.FieldMapping);
        Assert.Equal(connector.KeyVaultSecretName, msg.KeyVaultSecretName);
        Assert.Equal(connector.AuthType, msg.AuthType);
        Assert.False(msg.IsBackfill);
    }

    // --- Service lifecycle tests ---

    [Fact]
    public async Task Service_DoesNotRun_WhenDisabled()
    {
        _settings.Enabled = false;

        AddConnector("* * * * *", ConnectorStatus.Enabled,
            lastScheduledSyncAt: DateTimeOffset.UtcNow.AddHours(-1));

        var service = CreateService(DateTimeOffset.UtcNow);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await service.StartAsync(cts.Token);
        await Task.Delay(300);
        await service.StopAsync(CancellationToken.None);

        Assert.Empty(_publisher.Published);
    }

    [Fact]
    public void Settings_DefaultValues_AreCorrect()
    {
        var settings = new ScheduledSyncSettings();
        Assert.True(settings.Enabled);
        Assert.Equal(60, settings.EvaluationIntervalSeconds);
    }

    [Fact]
    public async Task EvaluateSchedules_PropagatesCancellation()
    {
        AddConnector("0 * * * *", ConnectorStatus.Enabled,
            lastScheduledSyncAt: new DateTimeOffset(2026, 3, 18, 9, 0, 0, TimeSpan.Zero));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var now = new DateTimeOffset(2026, 3, 18, 10, 1, 0, TimeSpan.Zero);
        var service = CreateService(now);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.EvaluateSchedulesAsync(cts.Token));
    }

    // --- Helpers ---

    private ScheduledSyncService CreateService(DateTimeOffset fixedNow)
    {
        var timeProvider = new FakeTimeProvider(fixedNow);
        var logger = _sp.GetRequiredService<ILoggerFactory>().CreateLogger<ScheduledSyncService>();

        return new ScheduledSyncService(
            _sp.GetRequiredService<IServiceScopeFactory>(),
            _publisher,
            _settings,
            logger,
            timeProvider);
    }

    private void SeedTenant(string tenantId)
    {
        _db.Tenants.Add(new TenantEntity
        {
            TenantId = tenantId,
            DisplayName = "Test Tenant",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();
    }

    private ConnectorEntity AddConnector(
        string? cron, ConnectorStatus status,
        DateTimeOffset? lastScheduledSyncAt = null)
    {
        var entity = new ConnectorEntity
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-1",
            Name = $"Test-{Guid.NewGuid():N}",
            ConnectorType = ConnectorType.AzureDevOps,
            Status = status,
            AuthType = SecretAuthType.Pat,
            ScheduleCron = cron,
            LastScheduledSyncAt = lastScheduledSyncAt,
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.Connectors.Add(entity);
        _db.SaveChanges();
        return entity;
    }

    private static ConnectorEntity MakeConnector(
        string? cron, DateTimeOffset? lastScheduledSyncAt = null)
    {
        return new ConnectorEntity
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-1",
            Name = "Test",
            ConnectorType = ConnectorType.AzureDevOps,
            Status = ConnectorStatus.Enabled,
            AuthType = SecretAuthType.Pat,
            ScheduleCron = cron,
            LastScheduledSyncAt = lastScheduledSyncAt,
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedAt = DateTimeOffset.UtcNow,
            SyncRuns = new List<SyncRunEntity>(),
        };
    }

    private sealed class FakeSyncJobPublisher : ISyncJobPublisher
    {
        public List<SyncJobMessage> Published { get; } = [];

        public Task PublishAsync(SyncJobMessage message, CancellationToken cancellationToken = default)
        {
            Published.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FakeTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
