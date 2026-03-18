using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;

namespace SmartKb.Ingestion;

/// <summary>
/// Fallback publisher when Service Bus is not configured. Logs a warning instead of enqueuing.
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
            "Service Bus not configured. Scheduled sync job {SyncRunId} for connector {ConnectorId} was not enqueued.",
            message.SyncRunId, message.ConnectorId);
        return Task.CompletedTask;
    }
}
