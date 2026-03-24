using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using NCrontab;
using SmartKb.Contracts;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data;
using SmartKb.Data.Entities;

namespace SmartKb.Ingestion;

/// <summary>
/// Background service that evaluates connector ScheduleCron expressions
/// and publishes SyncJobMessages when due. Provides automatic sync cadence
/// for connectors that lack webhooks or during webhook outage recovery.
/// </summary>
public sealed class ScheduledSyncService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISyncJobPublisher _syncJobPublisher;
    private readonly ScheduledSyncSettings _settings;
    private readonly ILogger<ScheduledSyncService> _logger;
    private readonly TimeProvider _timeProvider;

    public ScheduledSyncService(
        IServiceScopeFactory scopeFactory,
        ISyncJobPublisher syncJobPublisher,
        ScheduledSyncSettings settings,
        ILogger<ScheduledSyncService> logger,
        TimeProvider? timeProvider = null)
    {
        _scopeFactory = scopeFactory;
        _syncJobPublisher = syncJobPublisher;
        _settings = settings;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("Scheduled sync service is disabled via configuration.");
            return;
        }

        _logger.LogInformation(
            "Scheduled sync service started. Evaluation interval: {IntervalSeconds}s",
            _settings.EvaluationIntervalSeconds);

        var interval = TimeSpan.FromSeconds(_settings.EvaluationIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EvaluateSchedulesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error during scheduled sync evaluation. Will retry next cycle.");
            }

            try
            {
                await Task.Delay(interval, _timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Scheduled sync service stopping.");
    }

    internal async Task EvaluateSchedulesAsync(CancellationToken ct)
    {
        using var activity = Diagnostics.IngestionSource.StartActivity(
            "EvaluateScheduledSyncs", ActivityKind.Internal);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmartKbDbContext>();

        var connectors = await db.Connectors
            .Where(c => c.Status == ConnectorStatus.Enabled && c.ScheduleCron != null && c.ScheduleCron != "")
            .Include(c => c.SyncRuns)
            .ToListAsync(ct);

        activity?.SetTag("smartkb.scheduled_sync.connectors_evaluated", connectors.Count);

        if (connectors.Count == 0) return;

        var now = _timeProvider.GetUtcNow();
        var triggered = 0;

        foreach (var connector in connectors)
        {
            try
            {
                if (IsDue(connector, now, _logger))
                {
                    await TriggerScheduledSyncAsync(db, connector, now, ct);
                    triggered++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "Failed to evaluate/trigger scheduled sync for connector {ConnectorId} ({ConnectorName})",
                    connector.Id, connector.Name);
            }
        }

        activity?.SetTag("smartkb.scheduled_sync.triggered_count", triggered);

        if (triggered > 0)
        {
            _logger.LogInformation("Scheduled sync evaluation complete: {Triggered}/{Total} connectors triggered.",
                triggered, connectors.Count);
        }
    }

    internal static bool IsDue(ConnectorEntity connector, DateTimeOffset now, ILogger? logger = null)
    {
        CrontabSchedule schedule;
        try
        {
            schedule = CrontabSchedule.Parse(connector.ScheduleCron,
                new CrontabSchedule.ParseOptions { IncludingSeconds = false });
        }
        catch (CrontabException ex)
        {
            // Invalid cron — treated as "not due". Validation at connector save time prevents this in practice.
            logger?.LogWarning(ex, "Invalid cron expression '{Cron}' on connector {ConnectorId}; treating as not due.",
                connector.ScheduleCron, connector.Id);
            return false;
        }

        // Determine the reference point: last scheduled sync, last completed sync run, or connector creation.
        var referenceTime = connector.LastScheduledSyncAt
            ?? connector.SyncRuns?
                .Where(r => r.Status == SyncRunStatus.Completed)
                .OrderByDescending(r => r.CompletedAt)
                .FirstOrDefault()?.CompletedAt
            ?? connector.CreatedAt;

        // Find the next occurrence after the reference time.
        var nextOccurrence = schedule.GetNextOccurrence(referenceTime.UtcDateTime);

        return now.UtcDateTime >= nextOccurrence;
    }

    private async Task TriggerScheduledSyncAsync(
        SmartKbDbContext db, ConnectorEntity connector, DateTimeOffset now, CancellationToken ct)
    {
        var correlationId = Activity.Current?.Id ?? Guid.NewGuid().ToString();
        var idempotencyKey = $"scheduled-{connector.Id:N}-{now:yyyyMMddHHmm}";

        // Check idempotency: avoid duplicate triggers within the same minute.
        var existing = await db.SyncRuns
            .AnyAsync(s => s.IdempotencyKey == idempotencyKey, ct);

        if (existing)
        {
            _logger.LogDebug(
                "Skipping scheduled sync for connector {ConnectorId} — already triggered (key={IdempotencyKey})",
                connector.Id, idempotencyKey);
            return;
        }

        // Retrieve last checkpoint for incremental sync.
        string? lastCheckpoint = connector.SyncRuns?
            .Where(r => r.Status == SyncRunStatus.Completed && r.Checkpoint is not null)
            .OrderByDescending(r => r.CompletedAt)
            .FirstOrDefault()?.Checkpoint;

        var syncRun = new SyncRunEntity
        {
            Id = Guid.NewGuid(),
            ConnectorId = connector.Id,
            TenantId = connector.TenantId,
            Status = SyncRunStatus.Pending,
            IsBackfill = false,
            StartedAt = now,
            IdempotencyKey = idempotencyKey,
        };

        db.SyncRuns.Add(syncRun);

        // Update last scheduled sync timestamp.
        connector.LastScheduledSyncAt = now;
        connector.UpdatedAt = now;

        await db.SaveChangesAsync(ct);

        var message = new SyncJobMessage
        {
            SyncRunId = syncRun.Id,
            ConnectorId = connector.Id,
            TenantId = connector.TenantId,
            ConnectorType = connector.ConnectorType,
            IsBackfill = false,
            SourceConfig = connector.SourceConfig,
            FieldMapping = connector.FieldMapping,
            KeyVaultSecretName = connector.KeyVaultSecretName,
            AuthType = connector.AuthType,
            Checkpoint = lastCheckpoint,
            CorrelationId = correlationId,
            EnqueuedAt = now,
        };

        await _syncJobPublisher.PublishAsync(message, ct);

        Diagnostics.ScheduledSyncTriggeredTotal.Add(1,
            new KeyValuePair<string, object?>("smartkb.connector_type", connector.ConnectorType.ToString()));

        _logger.LogInformation(
            "Scheduled sync triggered for connector {ConnectorId} ({ConnectorName}, type={ConnectorType}, runId={SyncRunId})",
            connector.Id, connector.Name, connector.ConnectorType, syncRun.Id);
    }
}
