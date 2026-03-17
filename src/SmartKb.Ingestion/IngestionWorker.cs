using System.Diagnostics;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using SmartKb.Contracts;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Models;
using SmartKb.Ingestion.Processing;

namespace SmartKb.Ingestion;

public sealed class IngestionWorker : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ServiceBusClient? _serviceBusClient;
    private readonly ServiceBusSettings _settings;
    private readonly ILogger<IngestionWorker> _logger;

    public IngestionWorker(
        IServiceScopeFactory scopeFactory,
        ServiceBusSettings settings,
        ILogger<IngestionWorker> logger,
        ServiceBusClient? serviceBusClient = null)
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
        _logger = logger;
        _serviceBusClient = serviceBusClient;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Ingestion worker started at {Time}", DateTimeOffset.UtcNow);

        if (_serviceBusClient is null)
        {
            _logger.LogWarning(
                "Service Bus not configured. Ingestion worker running in idle mode. " +
                "Configure ServiceBus:FullyQualifiedNamespace (preferred) or ServiceBus:ConnectionString to enable queue processing.");

            // Idle loop for environments without Service Bus.
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            return;
        }

        var processor = _serviceBusClient.CreateProcessor(_settings.QueueName, new ServiceBusProcessorOptions
        {
            MaxConcurrentCalls = _settings.MaxConcurrentCalls,
            AutoCompleteMessages = false,
            PrefetchCount = _settings.MaxConcurrentCalls * 2,
        });

        processor.ProcessMessageAsync += ProcessMessageAsync;
        processor.ProcessErrorAsync += ProcessErrorAsync;

        await processor.StartProcessingAsync(stoppingToken);

        _logger.LogInformation("Service Bus processor started on queue '{Queue}'", _settings.QueueName);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }

        _logger.LogInformation("Ingestion worker stopping...");
        await processor.StopProcessingAsync(CancellationToken.None);
        await processor.DisposeAsync();
    }

    private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        SyncJobMessage? message = null;
        try
        {
            message = JsonSerializer.Deserialize<SyncJobMessage>(args.Message.Body.ToString(), JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize sync job message {MessageId}. Dead-lettering.",
                args.Message.MessageId);
            Diagnostics.DeadLetterTotal.Add(1,
                new KeyValuePair<string, object?>("smartkb.reason", "DeserializationFailed"));
            await args.DeadLetterMessageAsync(args.Message, "DeserializationFailed",
                $"Could not deserialize message body: {ex.Message}", args.CancellationToken);
            return;
        }

        if (message is null)
        {
            _logger.LogError("Deserialized message {MessageId} was null. Dead-lettering.", args.Message.MessageId);
            Diagnostics.DeadLetterTotal.Add(1,
                new KeyValuePair<string, object?>("smartkb.reason", "NullMessage"));
            await args.DeadLetterMessageAsync(args.Message, "NullMessage",
                "Deserialized message was null.", args.CancellationToken);
            return;
        }

        // Propagate distributed trace context from Service Bus message.
        using var activity = Diagnostics.IngestionSource.StartActivity(
            "ProcessSyncJob", ActivityKind.Consumer);

        if (!string.IsNullOrEmpty(args.Message.CorrelationId))
        {
            activity?.SetTag("messaging.correlation_id", args.Message.CorrelationId);
        }

        activity?.SetTag("messaging.message_id", args.Message.MessageId);
        activity?.SetTag("messaging.delivery_count", args.Message.DeliveryCount);
        activity?.SetTag("smartkb.sync_run_id", message.SyncRunId.ToString());
        activity?.SetTag("smartkb.connector_id", message.ConnectorId.ToString());
        activity?.SetTag("smartkb.tenant_id", message.TenantId);

        _logger.LogInformation(
            "Processing sync job {SyncRunId} for connector {ConnectorId} (delivery {DeliveryCount})",
            message.SyncRunId, message.ConnectorId, args.Message.DeliveryCount);

        using var scope = _scopeFactory.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<SyncJobProcessor>();

        var success = await processor.ProcessAsync(message, args.CancellationToken);

        if (success)
        {
            activity?.SetStatus(ActivityStatusCode.Ok);
            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
        }
        else
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Sync job processing failed");
            // Let Service Bus handle retry via delivery count / dead-letter after max attempts.
            await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken);
        }
    }

    private Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception,
            "Service Bus processing error. Source={Source}, Namespace={Namespace}, EntityPath={EntityPath}",
            args.ErrorSource, args.FullyQualifiedNamespace, args.EntityPath);
        return Task.CompletedTask;
    }
}
