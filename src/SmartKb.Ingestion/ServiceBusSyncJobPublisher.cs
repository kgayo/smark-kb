using System.Text.Json;
using Azure.Messaging.ServiceBus;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;

namespace SmartKb.Ingestion;

/// <summary>
/// Publishes sync job messages to the Service Bus ingestion queue.
/// Mirrors the API publisher to allow the ScheduledSyncService to enqueue sync jobs
/// from within the ingestion worker process.
/// </summary>
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
            "Published scheduled sync job {SyncRunId} for connector {ConnectorId} (tenant={TenantId}, type={ConnectorType})",
            message.SyncRunId, message.ConnectorId, message.TenantId, message.ConnectorType);
    }

    public async ValueTask DisposeAsync()
    {
        await _sender.DisposeAsync();
    }
}
