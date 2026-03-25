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
/// Handles incoming HubSpot webhook event payloads.
/// Validates HMAC-SHA256 signatures, deduplicates events, and triggers incremental syncs.
/// HubSpot sends an array of events per delivery.
/// </summary>
public sealed class HubSpotWebhookHandler
{
    private readonly SmartKbDbContext _db;
    private readonly ISyncJobPublisher _syncJobPublisher;
    private readonly IAuditEventWriter _auditWriter;
    private readonly ISecretProvider? _secretProvider;
    private readonly WebhookSettings _webhookSettings;
    private readonly ILogger<HubSpotWebhookHandler> _logger;

    public HubSpotWebhookHandler(
        SmartKbDbContext db,
        ISyncJobPublisher syncJobPublisher,
        IAuditEventWriter auditWriter,
        WebhookSettings webhookSettings,
        ILogger<HubSpotWebhookHandler> logger,
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
    /// Process incoming HubSpot webhook events for the given connector.
    /// Returns (statusCode, message) for the HTTP response.
    /// </summary>
    public async Task<(int StatusCode, string Message)> HandleAsync(
        Guid connectorId, string requestBody,
        string? signatureHeader, string? timestampHeader,
        CancellationToken cancellationToken = default)
    {
        // 1. Look up connector.
        var connector = await _db.Connectors
            .Include(c => c.SyncRuns)
            .FirstOrDefaultAsync(c => c.Id == connectorId, cancellationToken);

        if (connector is null)
        {
            _logger.LogWarning("HubSpot webhook received for unknown connector {ConnectorId}", connectorId);
            return (404, ResponseMessages.ConnectorNotFound);
        }

        if (connector.Status != ConnectorStatus.Enabled)
        {
            _logger.LogInformation("HubSpot webhook received for disabled connector {ConnectorId}", connectorId);
            return (200, ResponseMessages.ConnectorDisabledEventIgnored);
        }

        // 2. Find active webhook subscriptions.
        var subscriptions = await _db.Set<WebhookSubscriptionEntity>()
            .Where(w => w.ConnectorId == connectorId && w.IsActive)
            .ToListAsync(cancellationToken);

        if (subscriptions.Count == 0)
        {
            _logger.LogWarning("HubSpot webhook received but no active subscriptions for connector {ConnectorId}", connectorId);
            return (200, ResponseMessages.NoActiveWebhookSubscriptions);
        }

        // 3. Validate HMAC-SHA256 signature.
        var subscription = subscriptions[0];
        if (!string.IsNullOrEmpty(subscription.WebhookSecretName) && _secretProvider is not null)
        {
            try
            {
                var secret = await _secretProvider.GetSecretAsync(subscription.WebhookSecretName, cancellationToken);
                if (!HubSpotWebhookManager.ValidateSignature(requestBody, signatureHeader, secret, timestampHeader, _logger))
                {
                    _logger.LogWarning(
                        "HubSpot webhook signature verification failed for connector {ConnectorId}", connectorId);

                    await _auditWriter.WriteAsync(new AuditEvent(
                        EventId: Guid.NewGuid().ToString(),
                        EventType: AuditEventTypes.WebhookSignatureFailed,
                        TenantId: connector.TenantId,
                        ActorId: ResponseMessages.SystemActorId,
                        CorrelationId: Guid.NewGuid().ToString(),
                        Timestamp: DateTimeOffset.UtcNow,
                        Detail: $"HubSpot webhook signature verification failed for connector '{connector.Name}' (id={connectorId})."));

                    return (401, ResponseMessages.InvalidWebhookSignature);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to retrieve webhook secret for connector {ConnectorId}", connectorId);
                return (500, ResponseMessages.FailedToVerifyWebhookSignature);
            }
        }

        // 4. Parse the webhook payload (HubSpot sends an array of events).
        List<HubSpotWebhookEvent>? events;
        try
        {
            events = JsonSerializer.Deserialize<List<HubSpotWebhookEvent>>(requestBody, SharedJsonOptions.CamelCase);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid HubSpot webhook payload for connector {ConnectorId}", connectorId);
            return (400, ResponseMessages.InvalidWebhookPayload);
        }

        if (events is null || events.Count == 0)
        {
            return (400, "Empty event array in webhook payload.");
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
        // Deduplicate: use the first event's ID as the idempotency key.
        var firstEvent = events[0];
        var correlationId = $"hubspot-webhook-{firstEvent.EventId}";

        // Get last completed checkpoint for incremental sync.
        var lastCheckpoint = connector.SyncRuns?
            .Where(r => r.Status == SyncRunStatus.Completed && r.Checkpoint is not null)
            .OrderByDescending(r => r.CompletedAt)
            .FirstOrDefault()?.Checkpoint;

        var eventTypes = string.Join(",", events.Select(e => e.SubscriptionType).Distinct());

        var syncRun = new SyncRunEntity
        {
            Id = Guid.NewGuid(),
            ConnectorId = connector.Id,
            TenantId = connector.TenantId,
            Status = SyncRunStatus.Pending,
            IsBackfill = false,
            StartedAt = DateTimeOffset.UtcNow,
            IdempotencyKey = $"hubspot-{firstEvent.SubscriptionType}-{firstEvent.EventId}",
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
            ActorId: ResponseMessages.SystemActorId,
            CorrelationId: correlationId,
            Timestamp: DateTimeOffset.UtcNow,
            Detail: $"HubSpot webhook events [{eventTypes}] received for connector '{connector.Name}' (id={connectorId}). " +
                    $"{events.Count} event(s). Incremental sync triggered (runId={syncRun.Id})."));

        _logger.LogInformation(
            "HubSpot webhook processed: connector={ConnectorId}, events={EventCount}, types={EventTypes}, syncRunId={SyncRunId}",
            connectorId, events.Count, eventTypes, syncRun.Id);

        return (200, $"Incremental sync triggered (runId={syncRun.Id}).");
    }

    /// <summary>
    /// Records a webhook delivery failure and activates polling fallback if threshold exceeded.
    /// </summary>
    public Task RecordDeliveryFailureAsync(
        Guid connectorId, CancellationToken cancellationToken = default)
        => WebhookFailureHelper.RecordDeliveryFailureAsync(
            _db, _webhookSettings, connectorId, "HubSpot", _logger, cancellationToken);

    internal DateTimeOffset ComputeNextPollTime(DateTimeOffset from)
        => WebhookFailureHelper.ComputeNextPollTime(_webhookSettings, from);
}
