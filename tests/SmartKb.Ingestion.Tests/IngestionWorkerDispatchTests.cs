using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Ingestion;

namespace SmartKb.Ingestion.Tests;

/// <summary>
/// Tests for IngestionWorker behavior: idle mode, graceful shutdown,
/// and message serialization validation.
/// Note: Full Service Bus dispatch loop testing requires ServiceBusProcessor
/// which cannot be unit-tested without a real Service Bus. These tests cover
/// the surrounding logic and contract validation.
/// </summary>
public class IngestionWorkerDispatchTests
{
    [Fact]
    public async Task Worker_StartsAndStops_Gracefully_InIdleMode()
    {
        // Arrange: no ServiceBusClient configured.
        var services = new ServiceCollection();
        services.AddLogging();
        var sp = services.BuildServiceProvider();

        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<IngestionWorker>();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var settings = new ServiceBusSettings();

        var worker = new IngestionWorker(scopeFactory, settings, logger);

        // Act: start and cancel quickly.
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await worker.StartAsync(cts.Token);
        await Task.Delay(200);
        await worker.StopAsync(CancellationToken.None);

        // Assert: worker did not throw. If we got here, idle mode worked.
    }

    [Fact]
    public async Task Worker_AcceptsNullServiceBusClient()
    {
        // Arrange: explicitly pass null ServiceBusClient.
        var services = new ServiceCollection();
        services.AddLogging();
        var sp = services.BuildServiceProvider();

        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<IngestionWorker>();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var settings = new ServiceBusSettings();

        // Act: should not throw.
        var worker = new IngestionWorker(scopeFactory, settings, logger, serviceBusClient: null);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await worker.StartAsync(cts.Token);
        await Task.Delay(150);
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void SyncJobMessage_RoundTrips_ThroughJson()
    {
        // This validates the serialization contract used by IngestionWorker.ProcessMessageAsync.
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        var original = new SyncJobMessage
        {
            SyncRunId = Guid.NewGuid(),
            ConnectorId = Guid.NewGuid(),
            TenantId = "tenant-test",
            ConnectorType = ConnectorType.AzureDevOps,
            IsBackfill = true,
            SourceConfig = """{"organizationUrl":"https://dev.azure.com/test"}""",
            FieldMapping = null,
            KeyVaultSecretName = "my-secret",
            AuthType = SecretAuthType.Pat,
            CorrelationId = "corr-123",
            EnqueuedAt = DateTimeOffset.UtcNow,
        };

        var json = JsonSerializer.Serialize(original, options);
        var deserialized = JsonSerializer.Deserialize<SyncJobMessage>(json, options);

        Assert.NotNull(deserialized);
        Assert.Equal(original.SyncRunId, deserialized!.SyncRunId);
        Assert.Equal(original.ConnectorId, deserialized.ConnectorId);
        Assert.Equal(original.TenantId, deserialized.TenantId);
        Assert.Equal(original.ConnectorType, deserialized.ConnectorType);
        Assert.Equal(original.IsBackfill, deserialized.IsBackfill);
        Assert.Equal(original.KeyVaultSecretName, deserialized.KeyVaultSecretName);
        Assert.Equal(original.CorrelationId, deserialized.CorrelationId);
    }

    [Fact]
    public void SyncJobMessage_DeserializesNull_FromInvalidJson()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        // IngestionWorker dead-letters messages that fail deserialization.
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<SyncJobMessage>("not-valid-json", options));
    }

    [Fact]
    public void SyncJobMessage_DeserializesNull_FromNullJsonLiteral()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        // IngestionWorker dead-letters null messages.
        var result = JsonSerializer.Deserialize<SyncJobMessage>("null", options);
        Assert.Null(result);
    }

    [Fact]
    public void ServiceBusSettings_DefaultValues()
    {
        var settings = new ServiceBusSettings();

        Assert.Equal("ingestion-jobs", settings.QueueName);
        Assert.True(settings.MaxConcurrentCalls > 0);
    }
}
