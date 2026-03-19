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

public sealed class AdoWebhookHandlerTests : IAsyncLifetime
{
    private SmartKbDbContext _db = null!;
    private TestSyncJobPublisher _publisher = null!;
    private InMemoryAuditWriter _auditWriter = null!;
    private InMemorySecretProvider _secretProvider = null!;
    private AdoWebhookHandler _handler = null!;
    private Guid _connectorId;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<SmartKbDbContext>()
            .UseInMemoryDatabase($"webhook-tests-{Guid.NewGuid()}")
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

        _handler = new AdoWebhookHandler(
            _db, _publisher, _auditWriter, settings,
            NullLogger<AdoWebhookHandler>.Instance, _secretProvider);

        // Seed test data.
        var tenant = new TenantEntity { TenantId = "t1", DisplayName = "Test", CreatedAt = DateTimeOffset.UtcNow };
        _db.Tenants.Add(tenant);

        _connectorId = Guid.NewGuid();
        _db.Connectors.Add(new ConnectorEntity
        {
            Id = _connectorId,
            TenantId = "t1",
            Name = "ado-test",
            ConnectorType = ConnectorType.AzureDevOps,
            Status = ConnectorStatus.Enabled,
            AuthType = SecretAuthType.Pat,
            KeyVaultSecretName = "ado-pat",
            SourceConfig = """{"organizationUrl":"https://dev.azure.com/test","projects":["MyProject"]}""",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        // Add active webhook subscription.
        _secretProvider.Secrets["webhook-secret"] = "test-shared-secret";
        _db.Set<WebhookSubscriptionEntity>().Add(new WebhookSubscriptionEntity
        {
            Id = Guid.NewGuid(),
            ConnectorId = _connectorId,
            TenantId = "t1",
            ExternalSubscriptionId = "ext-sub-1",
            EventType = "workitem.updated",
            CallbackUrl = "https://test.example.com/api/webhooks/ado/" + _connectorId,
            WebhookSecretName = "webhook-secret",
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
        var (status, _) = await _handler.HandleAsync(Guid.NewGuid(), "{}", null);
        Assert.Equal(404, status);
    }

    [Fact]
    public async Task HandleAsync_Returns200_WhenConnectorDisabled()
    {
        var connector = await _db.Connectors.FindAsync(_connectorId);
        connector!.Status = ConnectorStatus.Disabled;
        await _db.SaveChangesAsync();

        var (status, msg) = await _handler.HandleAsync(_connectorId, "{}", null);
        Assert.Equal(200, status);
        Assert.Contains("disabled", msg, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_publisher.PublishedMessages);
    }

    [Fact]
    public async Task HandleAsync_Returns401_WhenSignatureInvalid()
    {
        var payload = BuildPayload("workitem.updated");
        var wrongAuth = CreateBasicAuth("wrong-secret");

        var (status, _) = await _handler.HandleAsync(_connectorId, payload, wrongAuth);
        Assert.Equal(401, status);
        Assert.Empty(_publisher.PublishedMessages);

        // Verify audit event for failed signature.
        Assert.Contains(_auditWriter.Events, e => e.EventType == AuditEventTypes.WebhookSignatureFailed);
    }

    [Fact]
    public async Task HandleAsync_TriggersSync_WhenSignatureValid()
    {
        var payload = BuildPayload("workitem.updated");
        var auth = CreateBasicAuth("test-shared-secret");

        var (status, msg) = await _handler.HandleAsync(_connectorId, payload, auth);
        Assert.Equal(200, status);
        Assert.Contains("sync triggered", msg, StringComparison.OrdinalIgnoreCase);

        // Verify sync job was published.
        Assert.Single(_publisher.PublishedMessages);
        var message = _publisher.PublishedMessages[0];
        Assert.Equal(_connectorId, message.ConnectorId);
        Assert.Equal("t1", message.TenantId);
        Assert.False(message.IsBackfill);

        // Verify sync run was created.
        var syncRun = await _db.SyncRuns.FirstOrDefaultAsync(s => s.ConnectorId == _connectorId);
        Assert.NotNull(syncRun);
        Assert.Equal(SyncRunStatus.Pending, syncRun.Status);
        Assert.StartsWith("webhook-", syncRun.IdempotencyKey);

        // Verify audit event.
        Assert.Contains(_auditWriter.Events, e => e.EventType == AuditEventTypes.WebhookReceived);
    }

    [Fact]
    public async Task HandleAsync_Returns400_WhenPayloadInvalid()
    {
        var auth = CreateBasicAuth("test-shared-secret");
        var (status, _) = await _handler.HandleAsync(_connectorId, "not-json!!!", auth);
        Assert.Equal(400, status);
        Assert.Empty(_publisher.PublishedMessages);
    }

    [Fact]
    public async Task HandleAsync_Returns400_WhenEventTypeMissing()
    {
        var auth = CreateBasicAuth("test-shared-secret");
        var (status, _) = await _handler.HandleAsync(_connectorId, """{"id":"123"}""", auth);
        Assert.Equal(400, status);
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

        var payload = BuildPayload("workitem.updated");
        var auth = CreateBasicAuth("test-shared-secret");

        await _handler.HandleAsync(_connectorId, payload, auth);

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

        var payload = BuildPayload("workitem.created");
        var (status, _) = await _handler.HandleAsync(_connectorId, payload, null);
        Assert.Equal(200, status);
        Assert.Single(_publisher.PublishedMessages);
    }

    [Fact]
    public async Task RecordDeliveryFailure_ActivatesFallback_AfterThreshold()
    {
        // Record 3 failures (threshold = 3).
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

    private static string BuildPayload(string eventType, int notificationId = 1) =>
        $$$"""{"eventType":"{{{eventType}}}","notificationId":{{{notificationId}}},"id":"evt-123","publisherId":"tfs","resource":{"id":42}}""";

    private static string CreateBasicAuth(string password)
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes($":{password}"));
        return $"Basic {encoded}";
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
