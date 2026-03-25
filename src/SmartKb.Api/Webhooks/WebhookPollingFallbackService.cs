using Microsoft.EntityFrameworkCore;
using SmartKb.Contracts;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data;
using SmartKb.Data.Entities;

namespace SmartKb.Api.Webhooks;

/// <summary>
/// Background service that polls for connectors in webhook fallback mode.
/// When webhooks are unavailable (consecutive delivery failures exceed threshold),
/// this service triggers incremental syncs at configured intervals with jitter.
/// </summary>
public sealed class WebhookPollingFallbackService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WebhookSettings _settings;
    private readonly ILogger<WebhookPollingFallbackService> _logger;

    public WebhookPollingFallbackService(
        IServiceScopeFactory scopeFactory,
        WebhookSettings settings,
        ILogger<WebhookPollingFallbackService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Check every 30 seconds for subscriptions that need polling.
        var checkInterval = TimeSpan.FromSeconds(30);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessDuePollsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error in webhook polling fallback service");
            }

            await Task.Delay(checkInterval, stoppingToken);
        }
    }

    internal async Task ProcessDuePollsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmartKbDbContext>();
        var syncPublisher = scope.ServiceProvider.GetRequiredService<ISyncJobPublisher>();
        var auditWriter = scope.ServiceProvider.GetRequiredService<IAuditEventWriter>();

        var now = DateTimeOffset.UtcNow;

        // Find subscriptions in polling fallback mode that are due for a poll.
        // Load active fallback subscriptions, then filter client-side for DateTimeOffset comparison
        // (SQLite provider cannot translate DateTimeOffset comparisons in WHERE).
        var allFallbackSubscriptions = await db.Set<WebhookSubscriptionEntity>()
            .Where(w => w.IsActive && w.PollingFallbackActive && w.NextPollAt != null)
            .ToListAsync(cancellationToken);

        var dueSubscriptions = allFallbackSubscriptions
            .Where(w => w.NextPollAt <= now)
            .ToList();

        if (dueSubscriptions.Count == 0) return;

        // Load connectors for the due subscriptions.
        var connectorIds = dueSubscriptions.Select(w => w.ConnectorId).Distinct().ToList();
        var connectors = await db.Connectors
            .Where(c => connectorIds.Contains(c.Id))
            .Include(c => c.SyncRuns)
            .ToListAsync(cancellationToken);
        var connectorMap = connectors.ToDictionary(c => c.Id);

        // Group by connector to avoid duplicate syncs.
        var connectorGroups = dueSubscriptions
            .GroupBy(w => w.ConnectorId)
            .ToList();

        foreach (var group in connectorGroups)
        {
            if (!connectorMap.TryGetValue(group.Key, out var connector))
                continue;
            if (connector.Status != ConnectorStatus.Enabled)
                continue;

            try
            {
                // Get last completed checkpoint.
                string? lastCheckpoint = connector.SyncRuns
                    .Where(r => r.Status == SyncRunStatus.Completed && r.Checkpoint is not null)
                    .OrderByDescending(r => r.CompletedAt)
                    .FirstOrDefault()?.Checkpoint;

                var correlationId = $"poll-fallback-{connector.Id}-{now:yyyyMMddHHmmss}";

                var syncRun = new SyncRunEntity
                {
                    Id = Guid.NewGuid(),
                    ConnectorId = connector.Id,
                    TenantId = connector.TenantId,
                    Status = SyncRunStatus.Pending,
                    IsBackfill = false,
                    StartedAt = now,
                    IdempotencyKey = correlationId,
                };

                db.SyncRuns.Add(syncRun);

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

                await syncPublisher.PublishAsync(message, cancellationToken);

                // Schedule next poll.
                foreach (var sub in group)
                {
                    var jitter = Random.Shared.Next(0, _settings.PollingJitterMaxSeconds + 1);
                    sub.NextPollAt = now.AddSeconds(_settings.PollingFallbackIntervalSeconds + jitter);
                    sub.UpdatedAt = now;
                }

                await db.SaveChangesAsync(cancellationToken);

                await auditWriter.WriteAsync(new AuditEvent(
                    EventId: Guid.NewGuid().ToString(),
                    EventType: AuditEventTypes.WebhookPollFallback,
                    TenantId: connector.TenantId,
                    ActorId: ResponseMessages.SystemActorId,
                    CorrelationId: correlationId,
                    Timestamp: now,
                    Detail: $"Polling fallback triggered for connector '{connector.Name}' (id={connector.Id}). SyncRunId={syncRun.Id}."));

                _logger.LogInformation(
                    "Polling fallback sync triggered: connector={ConnectorId}, syncRunId={SyncRunId}",
                    connector.Id, syncRun.Id);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "Failed to trigger polling fallback sync for connector {ConnectorId}", connector.Id);
            }
        }
    }
}
