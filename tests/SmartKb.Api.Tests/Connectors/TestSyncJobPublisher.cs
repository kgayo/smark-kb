using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;

namespace SmartKb.Api.Tests.Connectors;

public sealed class TestSyncJobPublisher : ISyncJobPublisher
{
    public List<SyncJobMessage> PublishedMessages { get; } = [];

    public Task PublishAsync(SyncJobMessage message, CancellationToken cancellationToken = default)
    {
        PublishedMessages.Add(message);
        return Task.CompletedTask;
    }
}
