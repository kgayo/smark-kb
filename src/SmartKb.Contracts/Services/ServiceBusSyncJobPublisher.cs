using System.Text.Json;
using Azure.Messaging.ServiceBus;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Models;
using Microsoft.Extensions.Logging;

namespace SmartKb.Contracts.Services;

public sealed class ServiceBusSyncJobPublisher : ISyncJobPublisher, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ServiceBusSender _sender;
    private readonly ILogger<ServiceBusSyncJobPublisher> _logger;

    public ServiceBusSyncJobPublisher(ServiceBusClient client, ServiceBusSettings settings, ILogger<ServiceBusSyncJobPublisher> logger)
    {
        _sender = client.CreateSender(settings.QueueName);
        _logger = logger;
    }

    public async Task PublishAsync(SyncJobMessage message, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(message, JsonOptions);
        var sbMessage = new ServiceBusMessage(json)
        {
            ContentType = "application/json",
            MessageId = message.SyncRunId.ToString(),
            CorrelationId = message.CorrelationId,
            Subject = $"sync:{message.ConnectorType}:{message.ConnectorId}",
        };
        sbMessage.ApplicationProperties["tenantId"] = message.TenantId;
        sbMessage.ApplicationProperties["connectorType"] = message.ConnectorType.ToString();
        sbMessage.ApplicationProperties["isBackfill"] = message.IsBackfill;

        await _sender.SendMessageAsync(sbMessage, cancellationToken);

        _logger.LogInformation(
            "Published sync job {SyncRunId} for connector {ConnectorId} (tenant={TenantId}, type={ConnectorType})",
            message.SyncRunId, message.ConnectorId, message.TenantId, message.ConnectorType);
    }

    public async ValueTask DisposeAsync()
    {
        await _sender.DisposeAsync();
    }
}
