using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

/// <summary>
/// Publishes sync job messages to the ingestion queue.
/// </summary>
public interface ISyncJobPublisher
{
    Task PublishAsync(SyncJobMessage message, CancellationToken cancellationToken = default);
}
