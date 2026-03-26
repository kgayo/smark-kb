using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SmartKb.Api.Webhooks;
using SmartKb.Contracts;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data;
using SmartKb.Data.Entities;

namespace SmartKb.Api.Tests.Webhooks;

public sealed class HubSpotWebhookHandlerTests : IAsyncLifetime
{
    private SmartKbDbContext _db = null!;
    private TestSyncJobPublisher _publisher = null!;
    private InMemoryAuditWriter _auditWriter = null!;
    private InMemorySecretProvider _secretProvider = null!;
    private HubSpotWebhookHandler _handler = null!;
    private Guid _connectorId;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<SmartKbDbContext>()
            .UseInMemoryDatabase($"hubspot-webhook-tests-{Guid.NewGuid()}")
            .Options;

        _db = new SmartKbDbContext(options);
        await _db.Database.EnsureCreatedAsync();

        _publisher = new TestSyncJobPublisher();
        _auditWriter = new InMemoryAuditWriter();
        _secretProvider = new InMemorySecretProvider();

        var settings = new WebhookSettings
        {
            BaseCallbackUrl = "https://test.example.com",
            FailureThresholdForFallback = 3,
            PollingFallbackIntervalSeconds = 300,
            PollingJitterMaxSeconds = 60,
        };

        _handler = new HubSpotWebhookHandler(
            _db, _publisher, _auditWriter, settings,
            NullLogger<HubSpotWebhookHandler>.Instance, _secretProvider);

        // Seed test data.
        var tenant = new TenantEntity { TenantId = "t1", DisplayName = "Test", CreatedAt = DateTimeOffset.UtcNow };
        _db.Tenants.Add(tenant);

        _connectorId = Guid.NewGuid();
        _db.Connectors.Add(new ConnectorEntity
        {
            Id = _connectorId,
            TenantId = "t1",
            Name = "hubspot-test",
            ConnectorType = ConnectorType.HubSpot,
            Status = ConnectorStatus.Enabled,
            AuthType = SecretAuthType.Pat,
            KeyVaultSecretName = "hubspot-pat",
            SourceConfig = """{"portalId":"12345","objectTypes":["tickets"]}""",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        // Add active webhook subscription.
        _secretProvider.Secrets["hubspot-webhook-secret"] = "test-hubspot-secret";
        _db.Set<WebhookSubscriptionEntity>().Add(new WebhookSubscriptionEntity
        {
            Id = Guid.NewGuid(),
            ConnectorId = _connectorId,
            TenantId = "t1",
            ExternalSubscriptionId = "hs-sub-1",
            EventType = "ticket.creation",
            CallbackUrl = "https://test.example.com/api/webhooks/hubspot/" + _connectorId,
            WebhookSecretName = "hubspot-webhook-secret",
            IsActive = true,
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
    public async Task HandleAsync_Returns404_WhenConnectorNotFound()
    {
        var (status, _) = await _handler.HandleAsync(Guid.NewGuid(), "[]", null, null);
        Assert.Equal(404, status);
    }

    [Fact]
    public async Task HandleAsync_Returns200_WhenConnectorDisabled()
    {
        var connector = await _db.Connectors.FindAsync(_connectorId);
        connector!.Status = ConnectorStatus.Disabled;
        await _db.SaveChangesAsync();

        var (status, msg) = await _handler.HandleAsync(_connectorId, "[]", null, null);
        Assert.Equal(200, status);
        Assert.Contains("disabled", msg, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_publisher.PublishedMessages);
    }

    [Fact]
    public async Task HandleAsync_Returns401_WhenSignatureInvalid()
    {
        var payload = BuildEventArrayPayload();
        var wrongSignature = "deadbeef0000";

        var (status, _) = await _handler.HandleAsync(_connectorId, payload, wrongSignature, null);
        Assert.Equal(401, status);
        Assert.Empty(_publisher.PublishedMessages);

        // Verify audit event for failed signature.
        Assert.Contains(_auditWriter.Events, e => e.EventType == AuditEventTypes.WebhookSignatureFailed);
    }

    [Fact]
    public async Task HandleAsync_TriggersSync_WhenSignatureValid()
    {
        var payload = BuildEventArrayPayload();
        var signature = ComputeHmacSignature(payload, "test-hubspot-secret");

        var (status, msg) = await _handler.HandleAsync(_connectorId, payload, signature, null);
        Assert.Equal(200, status);
        Assert.Contains("sync triggered", msg, StringComparison.OrdinalIgnoreCase);

        // Verify sync job was published.
        Assert.Single(_publisher.PublishedMessages);
        var message = _publisher.PublishedMessages[0];
        Assert.Equal(_connectorId, message.ConnectorId);
        Assert.Equal("t1", message.TenantId);
        Assert.False(message.IsBackfill);
        Assert.Equal(ConnectorType.HubSpot, message.ConnectorType);

        // Verify sync run was created.
        var syncRun = await _db.SyncRuns.FirstOrDefaultAsync(s => s.ConnectorId == _connectorId);
        Assert.NotNull(syncRun);
        Assert.Equal(SyncRunStatus.Pending, syncRun.Status);
        Assert.StartsWith("hubspot-", syncRun.IdempotencyKey);

        // Verify audit event.
        Assert.Contains(_auditWriter.Events, e => e.EventType == AuditEventTypes.WebhookReceived);
    }

    [Fact]
    public async Task HandleAsync_Returns400_WhenPayloadInvalid()
    {
        var signature = ComputeHmacSignature("not-json!!!", "test-hubspot-secret");
        var (status, _) = await _handler.HandleAsync(_connectorId, "not-json!!!", signature, null);
        Assert.Equal(400, status);
        Assert.Empty(_publisher.PublishedMessages);
    }

    [Fact]
    public async Task HandleAsync_Returns400_WhenEventArrayEmpty()
    {
        var payload = "[]";
        var signature = ComputeHmacSignature(payload, "test-hubspot-secret");
        var (status, _) = await _handler.HandleAsync(_connectorId, payload, signature, null);
        Assert.Equal(400, status);
        Assert.Empty(_publisher.PublishedMessages);
    }

    [Fact]
    public async Task HandleAsync_Returns200_WhenNoActiveSubscriptions()
    {
        // Deactivate all subscriptions.
        var subs = await _db.Set<WebhookSubscriptionEntity>()
            .Where(w => w.ConnectorId == _connectorId)
            .ToListAsync();
        foreach (var sub in subs) sub.IsActive = false;
        await _db.SaveChangesAsync();

        var (status, msg) = await _handler.HandleAsync(_connectorId, "[]", null, null);
        Assert.Equal(200, status);
        Assert.Contains("No active", msg, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_publisher.PublishedMessages);
    }

    [Fact]
    public async Task HandleAsync_ResetsConsecutiveFailures_OnSuccess()
    {
        // Set up subscription with failures.
        var sub = await _db.Set<WebhookSubscriptionEntity>()
            .FirstAsync(w => w.ConnectorId == _connectorId);
        sub.ConsecutiveFailures = 5;
        sub.PollingFallbackActive = true;
        sub.NextPollAt = DateTimeOffset.UtcNow.AddMinutes(5);
        await _db.SaveChangesAsync();

        var payload = BuildEventArrayPayload();
        var signature = ComputeHmacSignature(payload, "test-hubspot-secret");
        await _handler.HandleAsync(_connectorId, payload, signature, null);

        var updatedSub = await _db.Set<WebhookSubscriptionEntity>()
            .FirstAsync(w => w.ConnectorId == _connectorId);
        Assert.Equal(0, updatedSub.ConsecutiveFailures);
        Assert.False(updatedSub.PollingFallbackActive);
        Assert.Null(updatedSub.NextPollAt);
        Assert.NotNull(updatedSub.LastDeliveryAt);
    }

    [Fact]
    public async Task HandleAsync_SkipsSignature_WhenNoSecretConfigured()
    {
        // Remove secret from subscription.
        var sub = await _db.Set<WebhookSubscriptionEntity>()
            .FirstAsync(w => w.ConnectorId == _connectorId);
        sub.WebhookSecretName = null;
        await _db.SaveChangesAsync();

        var payload = BuildEventArrayPayload();
        var (status, _) = await _handler.HandleAsync(_connectorId, payload, null, null);
        Assert.Equal(200, status);
        Assert.Single(_publisher.PublishedMessages);
    }

    [Fact]
    public async Task HandleAsync_IncludesCheckpoint_WhenPreviousSyncExists()
    {
        // Seed a completed sync run with checkpoint.
        _db.SyncRuns.Add(new SyncRunEntity
        {
            Id = Guid.NewGuid(),
            ConnectorId = _connectorId,
            TenantId = "t1",
            Status = SyncRunStatus.Completed,
            IsBackfill = false,
            StartedAt = DateTimeOffset.UtcNow.AddHours(-1),
            CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
            Checkpoint = """{"lastModified":"2026-03-01T00:00:00Z"}""",
            IdempotencyKey = "prev-run",
        });
        await _db.SaveChangesAsync();

        var payload = BuildEventArrayPayload();
        var signature = ComputeHmacSignature(payload, "test-hubspot-secret");
        await _handler.HandleAsync(_connectorId, payload, signature, null);

        Assert.Single(_publisher.PublishedMessages);
        var message = _publisher.PublishedMessages[0];
        Assert.NotNull(message.Checkpoint);
        Assert.Contains("lastModified", message.Checkpoint);
    }

    [Fact]
    public async Task HandleAsync_Returns500_WhenSecretRetrievalFails()
    {
        // Remove the secret from the in-memory provider so GetSecretAsync throws.
        _secretProvider.Secrets.Remove("hubspot-webhook-secret");

        var payload = BuildEventArrayPayload();
        var (status, msg) = await _handler.HandleAsync(_connectorId, payload, "any-signature", null);
        Assert.Equal(500, status);
        Assert.Contains("verify", msg, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_publisher.PublishedMessages);
    }

    [Fact]
    public async Task RecordDeliveryFailure_ActivatesFallback_AfterThreshold()
    {
        await _handler.RecordDeliveryFailureAsync(_connectorId);
        await _handler.RecordDeliveryFailureAsync(_connectorId);
        await _handler.RecordDeliveryFailureAsync(_connectorId);

        var sub = await _db.Set<WebhookSubscriptionEntity>()
            .FirstAsync(w => w.ConnectorId == _connectorId);

        Assert.Equal(3, sub.ConsecutiveFailures);
        Assert.True(sub.PollingFallbackActive);
        Assert.NotNull(sub.NextPollAt);
    }

    [Fact]
    public async Task RecordDeliveryFailure_DoesNotActivateFallback_BelowThreshold()
    {
        await _handler.RecordDeliveryFailureAsync(_connectorId);

        var sub = await _db.Set<WebhookSubscriptionEntity>()
            .FirstAsync(w => w.ConnectorId == _connectorId);

        Assert.Equal(1, sub.ConsecutiveFailures);
        Assert.False(sub.PollingFallbackActive);
    }

    [Fact]
    public void ComputeNextPollTime_AddsIntervalWithJitter()
    {
        var from = DateTimeOffset.UtcNow;
        var next = _handler.ComputeNextPollTime(from);

        // Should be between 300s and 360s from now (300 base + 0-60 jitter).
        Assert.True(next >= from.AddSeconds(300));
        Assert.True(next <= from.AddSeconds(361));
    }

    // --- Helpers ---

    private static string BuildEventArrayPayload(
        long eventId = 100,
        string subscriptionType = "ticket.creation",
        long portalId = 12345) =>
        $$"""[{"eventId":{{eventId}},"subscriptionId":1,"portalId":{{portalId}},"appId":99,"occurredAt":1234567890,"subscriptionType":"{{subscriptionType}}","attemptNumber":0,"objectId":42,"changeSource":"CRM","propertyName":"subject","propertyValue":"Help needed"}]""";

    private static string ComputeHmacSignature(string body, string secret)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var hmac = HMACSHA256.HashData(keyBytes, bodyBytes);
        return Convert.ToHexString(hmac).ToLowerInvariant();
    }

    [Fact]
    public async Task HandleAsync_ThrowsOperationCanceled_WhenCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _handler.HandleAsync(_connectorId, "{}", null, null, cts.Token));
    }

    // --- Test doubles ---

    private sealed class TestSyncJobPublisher : ISyncJobPublisher
    {
        public List<SyncJobMessage> PublishedMessages { get; } = [];
        public Task PublishAsync(SyncJobMessage message, CancellationToken ct = default)
        {
            PublishedMessages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryAuditWriter : IAuditEventWriter
    {
        public List<AuditEvent> Events { get; } = [];
        public Task WriteAsync(AuditEvent auditEvent, CancellationToken ct = default)
        {
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemorySecretProvider : ISecretProvider
    {
        public Dictionary<string, string> Secrets { get; } = [];
        public Task<string> GetSecretAsync(string secretName, CancellationToken ct = default)
        {
            if (Secrets.TryGetValue(secretName, out var val)) return Task.FromResult(val);
            throw new KeyNotFoundException(secretName);
        }
        public Task SetSecretAsync(string secretName, string secretValue, CancellationToken ct = default)
        {
            Secrets[secretName] = secretValue;
            return Task.CompletedTask;
        }
        public Task DeleteSecretAsync(string secretName, CancellationToken ct = default)
        {
            Secrets.Remove(secretName);
            return Task.CompletedTask;
        }
        public Task<SecretProperties?> GetSecretPropertiesAsync(string secretName, CancellationToken ct = default) =>
            Task.FromResult<SecretProperties?>(Secrets.ContainsKey(secretName) ? new SecretProperties(secretName, DateTimeOffset.UtcNow, null, null, true) : null);
    }
}
