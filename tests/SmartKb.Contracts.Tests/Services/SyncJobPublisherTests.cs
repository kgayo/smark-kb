using Microsoft.Extensions.Logging;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;

namespace SmartKb.Contracts.Tests.Services;

public sealed class SyncJobPublisherTests
{
    private static SyncJobMessage CreateTestMessage(
        Guid? syncRunId = null,
        Guid? connectorId = null,
        string tenantId = "tenant-1",
        ConnectorType connectorType = ConnectorType.AzureDevOps,
        bool isBackfill = false)
    {
        return new SyncJobMessage
        {
            SyncRunId = syncRunId ?? Guid.NewGuid(),
            ConnectorId = connectorId ?? Guid.NewGuid(),
            TenantId = tenantId,
            ConnectorType = connectorType,
            IsBackfill = isBackfill,
            CorrelationId = "corr-123",
            EnqueuedAt = DateTimeOffset.UtcNow,
        };
    }

    // --- InMemorySyncJobPublisher tests ---

    [Fact]
    public async Task InMemory_PublishAsync_LogsWarningWithSyncRunId()
    {
        var logger = new CapturingLogger<InMemorySyncJobPublisher>();
        var publisher = new InMemorySyncJobPublisher(logger);
        var message = CreateTestMessage();

        await publisher.PublishAsync(message);

        Assert.Single(logger.Entries);
        var (level, text) = logger.Entries[0];
        Assert.Equal(LogLevel.Warning, level);
        Assert.Contains(message.SyncRunId.ToString(), text);
        Assert.Contains(message.ConnectorId.ToString(), text);
    }

    [Fact]
    public async Task InMemory_PublishAsync_ReturnsCompletedTask()
    {
        var logger = new CapturingLogger<InMemorySyncJobPublisher>();
        var publisher = new InMemorySyncJobPublisher(logger);

        var task = publisher.PublishAsync(CreateTestMessage());

        Assert.True(task.IsCompleted);
        await task; // should not throw
    }

    [Fact]
    public async Task InMemory_PublishAsync_IncludesConfigurationGuidance()
    {
        var logger = new CapturingLogger<InMemorySyncJobPublisher>();
        var publisher = new InMemorySyncJobPublisher(logger);

        await publisher.PublishAsync(CreateTestMessage());

        Assert.Contains("Configure ServiceBus:ConnectionString", logger.Entries[0].Message);
    }

    [Fact]
    public async Task InMemory_PublishAsync_CancellationToken_DoesNotThrow()
    {
        var logger = new CapturingLogger<InMemorySyncJobPublisher>();
        var publisher = new InMemorySyncJobPublisher(logger);
        using var cts = new CancellationTokenSource();

        // InMemory publisher is synchronous, so cancellation doesn't apply
        await publisher.PublishAsync(CreateTestMessage(), cts.Token);

        Assert.Single(logger.Entries);
    }

    [Theory]
    [InlineData(ConnectorType.AzureDevOps)]
    [InlineData(ConnectorType.SharePoint)]
    [InlineData(ConnectorType.HubSpot)]
    [InlineData(ConnectorType.ClickUp)]
    public async Task InMemory_PublishAsync_WorksForAllConnectorTypes(ConnectorType connectorType)
    {
        var logger = new CapturingLogger<InMemorySyncJobPublisher>();
        var publisher = new InMemorySyncJobPublisher(logger);

        await publisher.PublishAsync(CreateTestMessage(connectorType: connectorType));

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, logger.Entries[0].Level);
    }

    // --- Capturing logger helper ---

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
