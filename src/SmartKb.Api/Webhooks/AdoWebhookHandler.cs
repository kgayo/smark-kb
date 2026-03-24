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
/// Handles incoming Azure DevOps service hook webhook payloads.
/// Validates HMAC signatures, deduplicates events, and triggers incremental syncs.
/// </summary>
public sealed class AdoWebhookHandler
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
    private readonly ILogger<AdoWebhookHandler> _logger;

    public AdoWebhookHandler(
        SmartKbDbContext db,
        ISyncJobPublisher syncJobPublisher,
        IAuditEventWriter auditWriter,
        WebhookSettings webhookSettings,
        ILogger<AdoWebhookHandler> logger,
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
    /// Process an incoming ADO webhook payload for the given connector.
    /// Returns (statusCode, message) for the HTTP response.
    /// </summary>
    public async Task<(int StatusCode, string Message)> HandleAsync(
        Guid connectorId, string requestBody, string? authorizationHeader,
        CancellationToken cancellationToken = default)
    {
        // 1. Look up connector (ignore global query filter for deleted connectors — they should 404).
        var connector = await _db.Connectors
            .Include(c => c.SyncRuns)
            .FirstOrDefaultAsync(c => c.Id == connectorId, cancellationToken);

        if (connector is null)
        {
            _logger.LogWarning("Webhook received for unknown connector {ConnectorId}", connectorId);
            return (404, "Connector not found.");
        }

        if (connector.Status != ConnectorStatus.Enabled)
        {
            _logger.LogInformation("Webhook received for disabled connector {ConnectorId}", connectorId);
            return (200, "Connector is disabled; event ignored.");
        }

        // 2. Find active webhook subscriptions for this connector.
        var subscriptions = await _db.Set<WebhookSubscriptionEntity>()
            .Where(w => w.ConnectorId == connectorId && w.IsActive)
            .ToListAsync(cancellationToken);

        if (subscriptions.Count == 0)
        {
            _logger.LogWarning("Webhook received but no active subscriptions for connector {ConnectorId}", connectorId);
            return (200, "No active webhook subscriptions.");
        }

        // 3. Validate HMAC signature using the webhook secret from Key Vault.
        var subscription = subscriptions[0]; // All subscriptions for a connector share the same secret.
        if (!string.IsNullOrEmpty(subscription.WebhookSecretName) && _secretProvider is not null)
        {
            try
            {
                var secret = await _secretProvider.GetSecretAsync(subscription.WebhookSecretName, cancellationToken);
                if (!ValidateSignature(requestBody, authorizationHeader, secret, _logger))
                {
                    _logger.LogWarning(
                        "Webhook signature verification failed for connector {ConnectorId}", connectorId);

                    await _auditWriter.WriteAsync(new AuditEvent(
                        EventId: Guid.NewGuid().ToString(),
                        EventType: AuditEventTypes.WebhookSignatureFailed,
                        TenantId: connector.TenantId,
                        ActorId: "system",
                        CorrelationId: Guid.NewGuid().ToString(),
                        Timestamp: DateTimeOffset.UtcNow,
                        Detail: $"Webhook signature verification failed for connector '{connector.Name}' (id={connectorId})."));

                    return (401, "Invalid webhook signature.");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to retrieve webhook secret for connector {ConnectorId}", connectorId);
                return (500, "Failed to verify webhook signature.");
            }
        }

        // 4. Parse the webhook payload.
        AdoWebhookPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<AdoWebhookPayload>(requestBody, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid webhook payload for connector {ConnectorId}", connectorId);
            return (400, "Invalid webhook payload.");
        }

        if (payload?.EventType is null)
        {
            return (400, "Missing eventType in webhook payload.");
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
        var correlationId = $"webhook-{payload.Id ?? Guid.NewGuid().ToString()}";

        // Get last completed checkpoint for incremental sync.
        string? lastCheckpoint = null;
        var lastCompleted = connector.SyncRuns?
            .Where(r => r.Status == SyncRunStatus.Completed && r.Checkpoint is not null)
            .OrderByDescending(r => r.CompletedAt)
            .FirstOrDefault();
        lastCheckpoint = lastCompleted?.Checkpoint;

        var syncRun = new SyncRunEntity
        {
            Id = Guid.NewGuid(),
            ConnectorId = connector.Id,
            TenantId = connector.TenantId,
            Status = SyncRunStatus.Pending,
            IsBackfill = false,
            StartedAt = DateTimeOffset.UtcNow,
            IdempotencyKey = $"webhook-{payload.EventType}-{payload.NotificationId}",
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
            Detail: $"Webhook event '{payload.EventType}' received for connector '{connector.Name}' (id={connectorId}). Incremental sync triggered (runId={syncRun.Id})."));

        _logger.LogInformation(
            "Webhook processed: connector={ConnectorId}, event={EventType}, syncRunId={SyncRunId}",
            connectorId, payload.EventType, syncRun.Id);

        return (200, $"Incremental sync triggered (runId={syncRun.Id}).");
    }

    /// <summary>
    /// Validates the HMAC signature from the ADO service hook.
    /// ADO sends the shared secret as basic auth password in the Authorization header.
    /// </summary>
    internal static bool ValidateSignature(string requestBody, string? authorizationHeader, string? expectedSecret, ILogger? logger = null)
    {
        if (string.IsNullOrEmpty(expectedSecret))
            return true; // No secret configured = skip verification.

        if (string.IsNullOrEmpty(authorizationHeader))
            return false;

        // ADO service hooks use Basic auth with the shared secret as password.
        // Authorization: Basic base64(:secret)
        if (!authorizationHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            var encoded = authorizationHeader["Basic ".Length..].Trim();
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));

            // Format is "username:password" — ADO sends ":password"
            var colonIndex = decoded.IndexOf(':');
            if (colonIndex < 0) return false;

            var password = decoded[(colonIndex + 1)..];
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(password),
                Encoding.UTF8.GetBytes(expectedSecret));
        }
        catch (FormatException ex)
        {
            logger?.LogWarning(ex, "Malformed Basic auth header in ADO webhook signature validation");
            return false;
        }
    }

    /// <summary>
    /// Records a webhook delivery failure and activates polling fallback if threshold is exceeded.
    /// Called by the polling fallback service when webhook health checks fail.
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
                    "Webhook fallback activated: connector={ConnectorId}, failures={Failures}, nextPoll={NextPoll}",
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
