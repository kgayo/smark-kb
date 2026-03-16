using Azure.Messaging.ServiceBus;
using SmartKb.Contracts.Configuration;

namespace SmartKb.Api.Connectors;

public sealed record DeadLetterMessage(
    string MessageId,
    string? CorrelationId,
    string? Subject,
    string? DeadLetterReason,
    string? DeadLetterErrorDescription,
    int DeliveryCount,
    DateTimeOffset EnqueuedTime,
    string Body,
    IDictionary<string, object> ApplicationProperties);

public sealed record DeadLetterListResponse(
    IReadOnlyList<DeadLetterMessage> Messages,
    int Count);

public sealed class DeadLetterService : IAsyncDisposable
{
    private readonly ServiceBusReceiver _receiver;
    private readonly ILogger<DeadLetterService> _logger;

    public DeadLetterService(ServiceBusClient client, ServiceBusSettings settings, ILogger<DeadLetterService> logger)
    {
        var dlqPath = $"{settings.QueueName}/$deadletterqueue";
        _receiver = client.CreateReceiver(dlqPath, new ServiceBusReceiverOptions
        {
            ReceiveMode = ServiceBusReceiveMode.PeekLock,
        });
        _logger = logger;
    }

    public async Task<DeadLetterListResponse> PeekAsync(int maxMessages = 20, CancellationToken ct = default)
    {
        var messages = await _receiver.PeekMessagesAsync(maxMessages, cancellationToken: ct);
        var result = messages.Select(m => new DeadLetterMessage(
            m.MessageId,
            m.CorrelationId,
            m.Subject,
            m.DeadLetterReason,
            m.DeadLetterErrorDescription,
            m.DeliveryCount,
            m.EnqueuedTime,
            m.Body.ToString(),
            m.ApplicationProperties.ToDictionary(p => p.Key, p => p.Value)
        )).ToList();

        return new DeadLetterListResponse(result, result.Count);
    }

    public async ValueTask DisposeAsync()
    {
        await _receiver.DisposeAsync();
    }
}
