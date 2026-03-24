using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartKb.Contracts;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Connectors;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data;
using SmartKb.Data.Entities;

namespace SmartKb.Api.Webhooks;

/// <summary>
/// Handles incoming ClickUp webhook event payloads.
/// Validates HMAC-SHA256 signatures via X-Signature header, deduplicates events,
/// and triggers incremental syncs.
/// </summary>
public sealed class ClickUpWebhookHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly SmartKbDbContext _db;
    private readonly ISyncJobPublisher _syncJobPublisher;
    private readonly IAuditEventWriter _auditWriter;
    private readonly ISecretProvider? _secretProvider;
    private readonly WebhookSettings _webhookSettings;
    private readonly ILogger<ClickUpWebhookHandler> _logger;

    public ClickUpWebhookHandler(
        SmartKbDbContext db,
        ISyncJobPublisher syncJobPublisher,
        IAuditEventWriter auditWriter,
        WebhookSettings webhookSettings,
        ILogger<ClickUpWebhookHandler> logger,
        ISecretProvider? secretProvider = null)
    {
        _db = db;
        _syncJobPublisher = syncJobPublisher;
        _auditWriter = auditWriter;
        _webhookSettings = webhookSettings;
        _logger = logger;
        _secretProvider = secretProvider;
    }

    /// <summary>
    /// Process incoming ClickUp webhook event for the given connector.
    /// Returns (statusCode, message) for the HTTP response.
    /// </summary>
    public async Task<(int StatusCode, string Message)> HandleAsync(
        Guid connectorId, string requestBody,
        string? signatureHeader,
        CancellationToken cancellationToken = default)
    {
        // 1. Look up connector.
        var connector = await _db.Connectors
            .Include(c => c.SyncRuns)
            .FirstOrDefaultAsync(c => c.Id == connectorId, cancellationToken);

        if (connector is null)
        {
            _logger.LogWarning("ClickUp webhook received for unknown connector {ConnectorId}", connectorId);
            return (404, "Connector not found.");
        }

        if (connector.Status != ConnectorStatus.Enabled)
        {
            _logger.LogInformation("ClickUp webhook received for disabled connector {ConnectorId}", connectorId);
            return (200, "Connector is disabled; event ignored.");
        }

        // 2. Find active webhook subscriptions.
        var subscriptions = await _db.Set<WebhookSubscriptionEntity>()
            .Where(w => w.ConnectorId == connectorId && w.IsActive)
            .ToListAsync(cancellationToken);

        if (subscriptions.Count == 0)
        {
            _logger.LogWarning("ClickUp webhook received but no active subscriptions for connector {ConnectorId}", connectorId);
            return (200, "No active webhook subscriptions.");
        }

        // 3. Validate HMAC-SHA256 signature.
        var subscription = subscriptions[0];
        if (!string.IsNullOrEmpty(subscription.WebhookSecretName) && _secretProvider is not null)
        {
            try
            {
                var secret = await _secretProvider.GetSecretAsync(subscription.WebhookSecretName, cancellationToken);
                if (!ClickUpWebhookManager.ValidateSignature(requestBody, signatureHeader, secret, _logger))
                {
                    _logger.LogWarning(
                        "ClickUp webhook signature verification failed for connector {ConnectorId}", connectorId);

                    await _auditWriter.WriteAsync(new AuditEvent(
                        EventId: Guid.NewGuid().ToString(),
                        EventType: AuditEventTypes.WebhookSignatureFailed,
                        TenantId: connector.TenantId,
                        ActorId: "system",
                        CorrelationId: Guid.NewGuid().ToString(),
                        Timestamp: DateTimeOffset.UtcNow,
                        Detail: $"ClickUp webhook signature verification failed for connector '{connector.Name}' (id={connectorId})."));

                    return (401, "Invalid webhook signature.");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to retrieve webhook secret for connector {ConnectorId}", connectorId);
                return (500, "Failed to verify webhook signature.");
            }
        }

        // 4. Parse the webhook payload (ClickUp sends a single event per delivery).
        ClickUpWebhookEvent? webhookEvent;
        try
        {
            webhookEvent = JsonSerializer.Deserialize<ClickUpWebhookEvent>(requestBody, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid ClickUp webhook payload for connector {ConnectorId}", connectorId);
            return (400, "Invalid webhook payload.");
        }

        if (webhookEvent is null || string.IsNullOrEmpty(webhookEvent.Event))
        {
            return (400, "Empty or invalid event in webhook payload.");
        }

        // 5. Update subscription delivery tracking.
        foreach (var sub in subscriptions)
        {
            sub.LastDeliveryAt = DateTimeOffset.UtcNow;
            sub.ConsecutiveFailures = 0;
            sub.PollingFallbackActive = false;
            sub.NextPollAt = null;
            sub.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await _db.SaveChangesAsync(cancellationToken);

        // 6. Trigger incremental sync via Service Bus.
        var idempotencyKey = $"clickup-{webhookEvent.Event}-{webhookEvent.WebhookId}-{webhookEvent.TaskId ?? Guid.NewGuid().ToString()}";
        var correlationId = $"clickup-webhook-{webhookEvent.WebhookId}";

        // Get last completed checkpoint for incremental sync.
        var lastCheckpoint = connector.SyncRuns?
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
            StartedAt = DateTimeOffset.UtcNow,
            IdempotencyKey = idempotencyKey,
        };

        _db.SyncRuns.Add(syncRun);
        await _db.SaveChangesAsync(cancellationToken);

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
            EnqueuedAt = DateTimeOffset.UtcNow,
        };

        await _syncJobPublisher.PublishAsync(message, cancellationToken);

        await _auditWriter.WriteAsync(new AuditEvent(
            EventId: Guid.NewGuid().ToString(),
            EventType: AuditEventTypes.WebhookReceived,
            TenantId: connector.TenantId,
            ActorId: "system",
            CorrelationId: correlationId,
            Timestamp: DateTimeOffset.UtcNow,
            Detail: $"ClickUp webhook event [{webhookEvent.Event}] received for connector '{connector.Name}' (id={connectorId}). " +
                    $"Task: {webhookEvent.TaskId ?? "N/A"}. Incremental sync triggered (runId={syncRun.Id})."));

        _logger.LogInformation(
            "ClickUp webhook processed: connector={ConnectorId}, event={Event}, taskId={TaskId}, syncRunId={SyncRunId}",
            connectorId, webhookEvent.Event, webhookEvent.TaskId, syncRun.Id);

        return (200, $"Incremental sync triggered (runId={syncRun.Id}).");
    }

    /// <summary>
    /// Records a webhook delivery failure and activates polling fallback if threshold exceeded.
    /// </summary>
    public async Task RecordDeliveryFailureAsync(
        Guid connectorId, CancellationToken cancellationToken = default)
    {
        var subscriptions = await _db.Set<WebhookSubscriptionEntity>()
            .Where(w => w.ConnectorId == connectorId && w.IsActive)
            .ToListAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        foreach (var sub in subscriptions)
        {
            sub.ConsecutiveFailures++;
            sub.UpdatedAt = now;

            if (sub.ConsecutiveFailures >= _webhookSettings.FailureThresholdForFallback)
            {
                sub.PollingFallbackActive = true;
                sub.NextPollAt = ComputeNextPollTime(now);
                _logger.LogWarning(
                    "ClickUp webhook fallback activated: connector={ConnectorId}, failures={Failures}, nextPoll={NextPoll}",
                    connectorId, sub.ConsecutiveFailures, sub.NextPollAt);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    internal DateTimeOffset ComputeNextPollTime(DateTimeOffset from)
    {
        var intervalSeconds = _webhookSettings.PollingFallbackIntervalSeconds;
        var jitter = Random.Shared.Next(0, _webhookSettings.PollingJitterMaxSeconds + 1);
        return from.AddSeconds(intervalSeconds + jitter);
    }
}
