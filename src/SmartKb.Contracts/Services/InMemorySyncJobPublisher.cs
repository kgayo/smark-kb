using SmartKb.Contracts.Models;
using Microsoft.Extensions.Logging;

namespace SmartKb.Contracts.Services;

/// <summary>
/// Fallback publisher when Service Bus is not configured. Logs the message but does not deliver it.
/// Used in development/test environments without a live Service Bus.
/// </summary>
public sealed class InMemorySyncJobPublisher : ISyncJobPublisher
{
    private readonly ILogger<InMemorySyncJobPublisher> _logger;

    public InMemorySyncJobPublisher(ILogger<InMemorySyncJobPublisher> logger)
    {
        _logger = logger;
    }

    public Task PublishAsync(SyncJobMessage message, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            "Service Bus not configured. Sync job {SyncRunId} for connector {ConnectorId} was not enqueued. " +
            "Configure ServiceBus:ConnectionString to enable queue-based ingestion.",
            message.SyncRunId, message.ConnectorId);
        return Task.CompletedTask;
    }
}
