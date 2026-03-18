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

        await using var service = new DeadLetterService(mockClient, settings, logger);

        Assert.Equal("test-queue/$deadletterqueue", mockClient.LastReceiverPath);
    }

    [Fact]
    public async Task PeekAsync_ReturnsEmptyList_WhenNoMessages()
    {
        var settings = new ServiceBusSettings { QueueName = "empty-queue" };
        var mockClient = new MockServiceBusClient("empty-queue", messages: []);
        var logger = NullLogger<DeadLetterService>.Instance;

        await using var service = new DeadLetterService(mockClient, settings, logger);

        var result = await service.PeekAsync(maxMessages: 10);

        Assert.NotNull(result);
        Assert.Empty(result.Messages);
        Assert.Equal(0, result.Count);
    }

    [Fact]
    public async Task PeekAsync_ReturnsMessages_WithCorrectMapping()
    {
        var settings = new ServiceBusSettings { QueueName = "test-queue" };

        // ServiceBusModelFactory parameter order:
        // body, messageId, partitionKey, viaPartitionKey, sessionId, replyToSessionId,
        // timeToLive, correlationId, subject, to, contentType, replyTo,
        // scheduledEnqueueTime, properties, lockTokenGuid, deliveryCount,
        // lockedUntil, sequenceNumber, deadLetterSource, enqueuedSequenceNumber, enqueuedTime
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

        var mockClient = new MockServiceBusClient("test-queue", messages);
        var logger = NullLogger<DeadLetterService>.Instance;

        await using var service = new DeadLetterService(mockClient, settings, logger);

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

        var mockClient = new MockServiceBusClient("multi-queue", messages);
        var logger = NullLogger<DeadLetterService>.Instance;

        await using var service = new DeadLetterService(mockClient, settings, logger);

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

        await using var service = new DeadLetterService(mockClient, settings, logger);

        var result = await service.PeekAsync();

        Assert.Equal(20, mockClient.MockReceiver!.LastMaxMessages);
    }

    [Fact]
    public async Task DisposeAsync_DisposesReceiver()
    {
        var settings = new ServiceBusSettings { QueueName = "dispose-queue" };
        var mockClient = new MockServiceBusClient("dispose-queue", messages: []);
        var logger = NullLogger<DeadLetterService>.Instance;

        var service = new DeadLetterService(mockClient, settings, logger);

        await service.DisposeAsync();

        Assert.True(mockClient.MockReceiver!.WasDisposed);
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
/// Mock ServiceBusClient that returns a mock receiver.
/// </summary>
internal class MockServiceBusClient : ServiceBusClient
{
    private readonly ServiceBusReceivedMessage[] _messages;

    public string? LastReceiverPath { get; private set; }
    public MockServiceBusReceiver? MockReceiver { get; private set; }

    public MockServiceBusClient(string queueName, params ServiceBusReceivedMessage[] messages)
    {
        _messages = messages;
    }

    public override ServiceBusReceiver CreateReceiver(string queueName, ServiceBusReceiverOptions? options = null)
    {
        LastReceiverPath = queueName;
        MockReceiver = new MockServiceBusReceiver(_messages);
        return MockReceiver;
    }
}

/// <summary>
/// Mock ServiceBusReceiver that returns pre-configured messages from PeekMessagesAsync.
/// </summary>
internal class MockServiceBusReceiver : ServiceBusReceiver
{
    private readonly IReadOnlyList<ServiceBusReceivedMessage> _messages;

    public int LastMaxMessages { get; private set; }
    public bool WasDisposed { get; private set; }

    public MockServiceBusReceiver(IReadOnlyList<ServiceBusReceivedMessage> messages)
    {
        _messages = messages;
    }

    public override Task<IReadOnlyList<ServiceBusReceivedMessage>> PeekMessagesAsync(
        int maxMessages, long? fromSequenceNumber = null, CancellationToken cancellationToken = default)
    {
        LastMaxMessages = maxMessages;
        return Task.FromResult(_messages);
    }

    public override ValueTask DisposeAsync()
    {
        WasDisposed = true;
        return ValueTask.CompletedTask;
    }
}
