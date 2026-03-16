using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SmartKb.Api.Webhooks;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data;
using SmartKb.Data.Entities;

namespace SmartKb.Api.Tests.Webhooks;

public sealed class SharePointWebhookHandlerTests : IAsyncLifetime
{
    private SmartKbDbContext _db = null!;
    private TestSyncJobPublisher _publisher = null!;
    private InMemoryAuditWriter _auditWriter = null!;
    private InMemorySecretProvider _secretProvider = null!;
    private SharePointWebhookHandler _handler = null!;
    private Guid _connectorId;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<SmartKbDbContext>()
            .UseInMemoryDatabase($"sp-webhook-tests-{Guid.NewGuid()}")
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

        _handler = new SharePointWebhookHandler(
            _db, _publisher, _auditWriter, settings,
            NullLogger<SharePointWebhookHandler>.Instance, _secretProvider);

        // Seed test data.
        var tenant = new TenantEntity { TenantId = "t1", DisplayName = "Test", CreatedAt = DateTimeOffset.UtcNow };
        _db.Tenants.Add(tenant);

        _connectorId = Guid.NewGuid();
        _db.Connectors.Add(new ConnectorEntity
        {
            Id = _connectorId,
            TenantId = "t1",
            Name = "sp-test",
            ConnectorType = ConnectorType.SharePoint,
            Status = ConnectorStatus.Enabled,
            AuthType = SecretAuthType.OAuth,
            KeyVaultSecretName = "sp-client-secret",
            SourceConfig = """{"siteUrl":"https://contoso.sharepoint.com/sites/support","entraIdTenantId":"aad-t","clientId":"c"}""",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        // Add active webhook subscription.
        _secretProvider.Secrets["webhook-sp-secret"] = "client-state-secret-value";
        _db.Set<WebhookSubscriptionEntity>().Add(new WebhookSubscriptionEntity
        {
            Id = Guid.NewGuid(),
            ConnectorId = _connectorId,
            TenantId = "t1",
            ExternalSubscriptionId = "graph-sub-1",
            EventType = "driveItem.changed.drive-1",
            CallbackUrl = "https://test.example.com/api/webhooks/msgraph/" + _connectorId,
            WebhookSecretName = "webhook-sp-secret",
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

    // --- Validation Handshake ---

    [Fact]
    public void HandleValidation_ReturnsTokenAsPlainText()
    {
        var (status, contentType, body) = _handler.HandleValidation("my-validation-token-123");

        Assert.Equal(200, status);
        Assert.Equal("text/plain", contentType);
        Assert.Equal("my-validation-token-123", body);
    }

    // --- Notification Processing ---

    [Fact]
    public async Task HandleNotificationAsync_Returns404_WhenConnectorNotFound()
    {
        var (status, _) = await _handler.HandleNotificationAsync(Guid.NewGuid(), "{}");
        Assert.Equal(404, status);
    }

    [Fact]
    public async Task HandleNotificationAsync_Returns200_WhenConnectorDisabled()
    {
        var connector = await _db.Connectors.FindAsync(_connectorId);
        connector!.Status = ConnectorStatus.Disabled;
        await _db.SaveChangesAsync();

        var (status, msg) = await _handler.HandleNotificationAsync(_connectorId, BuildNotificationPayload());
        Assert.Equal(200, status);
        Assert.Contains("disabled", msg, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_publisher.PublishedMessages);
    }

    [Fact]
    public async Task HandleNotificationAsync_Returns401_WhenClientStateMismatch()
    {
        var payload = BuildNotificationPayload(clientState: "wrong-client-state");

        var (status, _) = await _handler.HandleNotificationAsync(_connectorId, payload);
        Assert.Equal(401, status);
        Assert.Empty(_publisher.PublishedMessages);

        // Verify audit event.
        Assert.Contains(_auditWriter.Events, e => e.EventType == "webhook.clientstate_mismatch");
    }

    [Fact]
    public async Task HandleNotificationAsync_TriggersSync_WhenClientStateValid()
    {
        var payload = BuildNotificationPayload(clientState: "client-state-secret-value");

        var (status, msg) = await _handler.HandleNotificationAsync(_connectorId, payload);
        Assert.Equal(200, status);
        Assert.Contains("sync triggered", msg, StringComparison.OrdinalIgnoreCase);

        // Verify sync job was published.
        Assert.Single(_publisher.PublishedMessages);
        var message = _publisher.PublishedMessages[0];
        Assert.Equal(_connectorId, message.ConnectorId);
        Assert.Equal("t1", message.TenantId);
        Assert.False(message.IsBackfill);
        Assert.Equal(ConnectorType.SharePoint, message.ConnectorType);

        // Verify sync run was created.
        var syncRun = await _db.SyncRuns.FirstOrDefaultAsync(s => s.ConnectorId == _connectorId);
        Assert.NotNull(syncRun);
        Assert.Equal(SyncRunStatus.Pending, syncRun.Status);
        Assert.StartsWith("msgraph-", syncRun.IdempotencyKey);

        // Verify audit event.
        Assert.Contains(_auditWriter.Events, e => e.EventType == "webhook.received");
    }

    [Fact]
    public async Task HandleNotificationAsync_Returns400_WhenPayloadInvalid()
    {
        var (status, _) = await _handler.HandleNotificationAsync(_connectorId, "not-json!!!");
        Assert.Equal(400, status);
        Assert.Empty(_publisher.PublishedMessages);
    }

    [Fact]
    public async Task HandleNotificationAsync_Returns400_WhenNoNotifications()
    {
        var (status, _) = await _handler.HandleNotificationAsync(_connectorId, """{"value":[]}""");
        Assert.Equal(400, status);
    }

    [Fact]
    public async Task HandleNotificationAsync_ResetsConsecutiveFailures_OnSuccess()
    {
        var sub = await _db.Set<WebhookSubscriptionEntity>()
            .FirstAsync(w => w.ConnectorId == _connectorId);
        sub.ConsecutiveFailures = 5;
        sub.PollingFallbackActive = true;
        sub.NextPollAt = DateTimeOffset.UtcNow.AddMinutes(5);
        await _db.SaveChangesAsync();

        var payload = BuildNotificationPayload(clientState: "client-state-secret-value");
        await _handler.HandleNotificationAsync(_connectorId, payload);

        var updatedSub = await _db.Set<WebhookSubscriptionEntity>()
            .FirstAsync(w => w.ConnectorId == _connectorId);
        Assert.Equal(0, updatedSub.ConsecutiveFailures);
        Assert.False(updatedSub.PollingFallbackActive);
        Assert.Null(updatedSub.NextPollAt);
        Assert.NotNull(updatedSub.LastDeliveryAt);
    }

    [Fact]
    public async Task HandleNotificationAsync_SkipsValidation_WhenNoSecretConfigured()
    {
        var sub = await _db.Set<WebhookSubscriptionEntity>()
            .FirstAsync(w => w.ConnectorId == _connectorId);
        sub.WebhookSecretName = null;
        await _db.SaveChangesAsync();

        // clientState doesn't match anything, but validation is skipped.
        var payload = BuildNotificationPayload(clientState: "anything");
        var (status, _) = await _handler.HandleNotificationAsync(_connectorId, payload);
        Assert.Equal(200, status);
        Assert.Single(_publisher.PublishedMessages);
    }

    // --- Delivery Failure ---

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

        Assert.True(next >= from.AddSeconds(300));
        Assert.True(next <= from.AddSeconds(361));
    }

    // --- Helpers ---

    private static string BuildNotificationPayload(
        string clientState = "client-state-secret-value",
        string changeType = "updated",
        string subscriptionId = "graph-sub-1")
    {
        return $$"""
        {
            "value": [{
                "subscriptionId": "{{subscriptionId}}",
                "clientState": "{{clientState}}",
                "changeType": "{{changeType}}",
                "resource": "/drives/drive-1/root",
                "resourceData": {
                    "@odata.type": "#Microsoft.Graph.DriveItem",
                    "id": "item-42"
                }
            }]
        }
        """;
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
    }
}
