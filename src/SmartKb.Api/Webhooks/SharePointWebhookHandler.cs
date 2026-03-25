using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
/// Handles incoming Microsoft Graph change notification payloads for SharePoint connectors.
/// Supports the Graph validation handshake (validationToken query param) and
/// verifies clientState to authenticate notifications.
/// </summary>
public sealed class SharePointWebhookHandler
{
    private readonly SmartKbDbContext _db;
    private readonly ISyncJobPublisher _syncJobPublisher;
    private readonly IAuditEventWriter _auditWriter;
    private readonly ISecretProvider? _secretProvider;
    private readonly WebhookSettings _webhookSettings;
    private readonly ILogger<SharePointWebhookHandler> _logger;

    public SharePointWebhookHandler(
        SmartKbDbContext db,
        ISyncJobPublisher syncJobPublisher,
        IAuditEventWriter auditWriter,
        WebhookSettings webhookSettings,
        ILogger<SharePointWebhookHandler> logger,
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
    /// Handles the Graph validation handshake: returns the validationToken as plain text.
    /// Graph sends this as a GET with ?validationToken=... when creating a subscription.
    /// </summary>
    public (int StatusCode, string? ContentType, string Body) HandleValidation(string validationToken)
    {
        _logger.LogInformation("Graph subscription validation handshake received.");
        return (200, "text/plain", validationToken);
    }

    /// <summary>
    /// Processes incoming Graph change notifications for the given connector.
    /// Returns (statusCode, message) for the HTTP response.
    /// </summary>
    public async Task<(int StatusCode, string Message)> HandleNotificationAsync(
        Guid connectorId, string requestBody,
        CancellationToken cancellationToken = default)
    {
        // 1. Look up connector.
        var connector = await _db.Connectors
            .Include(c => c.SyncRuns)
            .FirstOrDefaultAsync(c => c.Id == connectorId, cancellationToken);

        if (connector is null)
        {
            _logger.LogWarning("Graph notification received for unknown connector {ConnectorId}", connectorId);
            return (404, ResponseMessages.ConnectorNotFound);
        }

        if (connector.Status != ConnectorStatus.Enabled)
        {
            _logger.LogInformation("Graph notification received for disabled connector {ConnectorId}", connectorId);
            return (200, ResponseMessages.ConnectorDisabledEventIgnored);
        }

        // 2. Parse notification payload.
        GraphChangeNotificationPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<GraphChangeNotificationPayload>(requestBody, SharedJsonOptions.CamelCase);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid Graph notification payload for connector {ConnectorId}", connectorId);
            return (400, "Invalid notification payload.");
        }

        if (payload?.Value is null || payload.Value.Count == 0)
        {
            return (400, "No notifications in payload.");
        }

        // 3. Find active webhook subscriptions for this connector.
        var subscriptions = await _db.Set<WebhookSubscriptionEntity>()
            .Where(w => w.ConnectorId == connectorId && w.IsActive)
            .ToListAsync(cancellationToken);

        if (subscriptions.Count == 0)
        {
            _logger.LogWarning("Graph notification received but no active subscriptions for connector {ConnectorId}", connectorId);
            return (200, ResponseMessages.NoActiveWebhookSubscriptions);
        }

        // 4. Validate clientState for each notification.
        foreach (var notification in payload.Value)
        {
            if (!await ValidateClientStateAsync(notification, subscriptions, cancellationToken))
            {
                _logger.LogWarning(
                    "Graph notification clientState mismatch for connector {ConnectorId}, subscriptionId={SubscriptionId}",
                    connectorId, notification.SubscriptionId);

                await _auditWriter.WriteAsync(new AuditEvent(
                    EventId: Guid.NewGuid().ToString(),
                    EventType: AuditEventTypes.WebhookClientStateMismatch,
                    TenantId: connector.TenantId,
                    ActorId: ResponseMessages.SystemActorId,
                    CorrelationId: Guid.NewGuid().ToString(),
                    Timestamp: DateTimeOffset.UtcNow,
                    Detail: $"Graph notification clientState verification failed for connector '{connector.Name}' (id={connectorId})."));

                return (401, "Invalid clientState.");
            }
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
        var firstNotification = payload.Value[0];
        var correlationId = $"msgraph-{firstNotification.SubscriptionId ?? Guid.NewGuid().ToString()}";
        var changeTypes = string.Join(",", payload.Value.Select(n => n.ChangeType).Distinct());

        // Get last completed checkpoint for incremental sync.
        var lastCompleted = connector.SyncRuns?
            .Where(r => r.Status == SyncRunStatus.Completed && r.Checkpoint is not null)
            .OrderByDescending(r => r.CompletedAt)
            .FirstOrDefault();

        var syncRun = new SyncRunEntity
        {
            Id = Guid.NewGuid(),
            ConnectorId = connector.Id,
            TenantId = connector.TenantId,
            Status = SyncRunStatus.Pending,
            IsBackfill = false,
            StartedAt = DateTimeOffset.UtcNow,
            IdempotencyKey = $"msgraph-{firstNotification.SubscriptionId}-{DateTimeOffset.UtcNow.Ticks}",
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
            Checkpoint = lastCompleted?.Checkpoint,
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
            Detail: $"Graph change notification received for connector '{connector.Name}' (id={connectorId}). Changes: {changeTypes}. Incremental sync triggered (runId={syncRun.Id})."));

        _logger.LogInformation(
            "Graph notification processed: connector={ConnectorId}, changes={ChangeTypes}, syncRunId={SyncRunId}",
            connectorId, changeTypes, syncRun.Id);

        return (200, $"Incremental sync triggered (runId={syncRun.Id}).");
    }

    /// <summary>
    /// Validates the clientState field on a Graph notification against the stored webhook secret.
    /// </summary>
    private async Task<bool> ValidateClientStateAsync(
        GraphChangeNotification notification,
        List<WebhookSubscriptionEntity> subscriptions,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(notification.ClientState))
            return false;

        // Find matching subscription by external ID.
        var matchingSub = subscriptions.FirstOrDefault(s =>
            s.ExternalSubscriptionId == notification.SubscriptionId) ?? subscriptions[0];

        if (string.IsNullOrEmpty(matchingSub.WebhookSecretName) || _secretProvider is null)
            return true; // No secret configured — skip verification.

        try
        {
            var expectedSecret = await _secretProvider.GetSecretAsync(matchingSub.WebhookSecretName, ct);
            if (string.IsNullOrEmpty(expectedSecret))
                return true;

            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(notification.ClientState),
                Encoding.UTF8.GetBytes(expectedSecret));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to retrieve webhook secret for validation");
            return false;
        }
    }

    /// <summary>
    /// Records a webhook delivery failure and activates polling fallback if threshold is exceeded.
    /// </summary>
    public Task RecordDeliveryFailureAsync(
        Guid connectorId, CancellationToken cancellationToken = default)
        => WebhookFailureHelper.RecordDeliveryFailureAsync(
            _db, _webhookSettings, connectorId, "SharePoint", _logger, cancellationToken);

    internal DateTimeOffset ComputeNextPollTime(DateTimeOffset from)
        => WebhookFailureHelper.ComputeNextPollTime(_webhookSettings, from);
}
