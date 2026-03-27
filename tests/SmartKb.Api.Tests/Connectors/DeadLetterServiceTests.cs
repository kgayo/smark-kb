using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging.Abstractions;
using SmartKb.Api.Connectors;
using SmartKb.Contracts.Configuration;

namespace SmartKb.Api.Tests.Connectors;

public class DeadLetterServiceTests
{
    [Fact]
    public async Task Constructor_BuildsCorrectDlqPath()
    {
        var settings = new ServiceBusSettings { QueueName = "test-queue" };
        var mockClient = new MockServiceBusClient("test-queue");
        var logger = NullLogger<DeadLetterService>.Instance;

        var service = new DeadLetterService(mockClient, settings, logger);

        await service.PeekAsync(maxMessages: 1);

        Assert.Equal("test-queue/$deadletterqueue", mockClient.LastReceiverPath);
    }

    [Fact]
    public async Task PeekAsync_ReturnsEmptyList_WhenNoMessages()
    {
        var settings = new ServiceBusSettings { QueueName = "empty-queue" };
        var mockClient = new MockServiceBusClient("empty-queue", messages: []);
        var logger = NullLogger<DeadLetterService>.Instance;

        var service = new DeadLetterService(mockClient, settings, logger);

        var result = await service.PeekAsync(maxMessages: 10);

        Assert.NotNull(result);
        Assert.Empty(result.Messages);
        Assert.Equal(0, result.Count);
    }

    [Fact]
    public async Task PeekAsync_ReturnsMessages_WithCorrectMapping()
    {
        var settings = new ServiceBusSettings { QueueName = "test-queue" };

        var messages = new[]
        {
            ServiceBusModelFactory.ServiceBusReceivedMessage(
                body: BinaryData.FromString("""{"syncRunId":"abc"}"""),
                messageId: "msg-1",
                correlationId: "corr-1",
                subject: "SyncJob",
                deliveryCount: 3,
                enqueuedTime: new DateTimeOffset(2026, 3, 15, 10, 0, 0, TimeSpan.Zero)),
        };

        var mockClient = new MockServiceBusClient("test-queue", messages: messages);
        var logger = NullLogger<DeadLetterService>.Instance;

        var service = new DeadLetterService(mockClient, settings, logger);

        var result = await service.PeekAsync(maxMessages: 20);

        Assert.NotNull(result);
        Assert.Single(result.Messages);
        Assert.Equal(1, result.Count);

        var msg = result.Messages[0];
        Assert.Equal("msg-1", msg.MessageId);
        Assert.Equal("corr-1", msg.CorrelationId);
        Assert.Equal("SyncJob", msg.Subject);
        Assert.Equal(3, msg.DeliveryCount);
        Assert.Contains("syncRunId", msg.Body);
    }

    [Fact]
    public async Task PeekAsync_ReturnsMultipleMessages()
    {
        var settings = new ServiceBusSettings { QueueName = "multi-queue" };

        var messages = Enumerable.Range(1, 5).Select(i =>
            ServiceBusModelFactory.ServiceBusReceivedMessage(
                body: BinaryData.FromString($"{{\"id\":{i}}}"),
                messageId: $"msg-{i}",
                deliveryCount: i)).ToArray();

        var mockClient = new MockServiceBusClient("multi-queue", messages: messages);
        var logger = NullLogger<DeadLetterService>.Instance;

        var service = new DeadLetterService(mockClient, settings, logger);

        var result = await service.PeekAsync(maxMessages: 10);

        Assert.Equal(5, result.Count);
        Assert.Equal(5, result.Messages.Count);
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal($"msg-{i + 1}", result.Messages[i].MessageId);
        }
    }

    [Fact]
    public async Task PeekAsync_DefaultMaxMessages_Is20()
    {
        var settings = new ServiceBusSettings { QueueName = "default-queue" };
        var mockClient = new MockServiceBusClient("default-queue", messages: []);
        var logger = NullLogger<DeadLetterService>.Instance;

        var service = new DeadLetterService(mockClient, settings, logger);

        var result = await service.PeekAsync();

        Assert.Equal(20, mockClient.LastMockReceiver!.LastMaxMessages);
    }

    [Fact]
    public async Task PeekAsync_CreatesAndDisposesReceiverPerCall()
    {
        var settings = new ServiceBusSettings { QueueName = "per-call-queue" };
        var mockClient = new MockServiceBusClient("per-call-queue", messages: []);
        var logger = NullLogger<DeadLetterService>.Instance;

        var service = new DeadLetterService(mockClient, settings, logger);

        await service.PeekAsync(maxMessages: 5);
        var firstReceiver = mockClient.LastMockReceiver!;
        Assert.True(firstReceiver.WasDisposed);

        await service.PeekAsync(maxMessages: 10);
        var secondReceiver = mockClient.LastMockReceiver!;
        Assert.True(secondReceiver.WasDisposed);

        Assert.NotSame(firstReceiver, secondReceiver);
        Assert.Equal(2, mockClient.ReceiverCreationCount);
    }

    [Fact]
    public async Task PeekAsync_DisposesReceiver_EvenOnException()
    {
        var settings = new ServiceBusSettings { QueueName = "error-queue" };
        var mockClient = new MockServiceBusClient("error-queue", throwOnPeek: true);
        var logger = NullLogger<DeadLetterService>.Instance;

        var service = new DeadLetterService(mockClient, settings, logger);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PeekAsync());

        Assert.True(mockClient.LastMockReceiver!.WasDisposed);
    }

    [Fact]
    public void DeadLetterMessage_Record_Properties()
    {
        var props = new Dictionary<string, object> { ["key1"] = "value1" };
        var msg = new DeadLetterMessage(
            "msg-id", "corr-id", "subject", "reason", "desc",
            5, DateTimeOffset.UtcNow, "{}", props);

        Assert.Equal("msg-id", msg.MessageId);
        Assert.Equal("corr-id", msg.CorrelationId);
        Assert.Equal("subject", msg.Subject);
        Assert.Equal("reason", msg.DeadLetterReason);
        Assert.Equal("desc", msg.DeadLetterErrorDescription);
        Assert.Equal(5, msg.DeliveryCount);
        Assert.Equal("{}", msg.Body);
        Assert.Equal("value1", msg.ApplicationProperties["key1"]);
    }

    [Fact]
    public void DeadLetterListResponse_Record_Properties()
    {
        var messages = new List<DeadLetterMessage>
        {
            new("msg-1", null, null, null, null, 1, DateTimeOffset.UtcNow, "{}", new Dictionary<string, object>()),
            new("msg-2", null, null, null, null, 2, DateTimeOffset.UtcNow, "{}", new Dictionary<string, object>()),
        };

        var response = new DeadLetterListResponse(messages, 2);

        Assert.Equal(2, response.Count);
        Assert.Equal(2, response.Messages.Count);
    }
}

/// <summary>
/// Mock ServiceBusClient that creates a new mock receiver per call, tracking creation count.
/// </summary>
internal class MockServiceBusClient : ServiceBusClient
{
    private readonly ServiceBusReceivedMessage[] _messages;
    private readonly bool _throwOnPeek;

    public string? LastReceiverPath { get; private set; }
    public MockServiceBusReceiver? LastMockReceiver { get; private set; }
    public int ReceiverCreationCount { get; private set; }

    public MockServiceBusClient(string queueName, ServiceBusReceivedMessage[]? messages = null, bool throwOnPeek = false)
    {
        _messages = messages ?? [];
        _throwOnPeek = throwOnPeek;
    }

    public override ServiceBusReceiver CreateReceiver(string queueName, ServiceBusReceiverOptions? options = null)
    {
        LastReceiverPath = queueName;
        ReceiverCreationCount++;
        LastMockReceiver = new MockServiceBusReceiver(_messages ?? [], _throwOnPeek);
        return LastMockReceiver;
    }
}

/// <summary>
/// Mock ServiceBusReceiver that returns pre-configured messages from PeekMessagesAsync.
/// </summary>
internal class MockServiceBusReceiver : ServiceBusReceiver
{
    private readonly IReadOnlyList<ServiceBusReceivedMessage> _messages;
    private readonly bool _throwOnPeek;

    public int LastMaxMessages { get; private set; }
    public bool WasDisposed { get; private set; }

    public MockServiceBusReceiver(IReadOnlyList<ServiceBusReceivedMessage> messages, bool throwOnPeek = false)
    {
        _messages = messages;
        _throwOnPeek = throwOnPeek;
    }

    public override Task<IReadOnlyList<ServiceBusReceivedMessage>> PeekMessagesAsync(
        int maxMessages, long? fromSequenceNumber = null, CancellationToken cancellationToken = default)
    {
        LastMaxMessages = maxMessages;
        if (_throwOnPeek)
            throw new InvalidOperationException("Simulated peek failure");
        return Task.FromResult(_messages);
    }

    public override ValueTask DisposeAsync()
    {
        WasDisposed = true;
        return ValueTask.CompletedTask;
    }
}
