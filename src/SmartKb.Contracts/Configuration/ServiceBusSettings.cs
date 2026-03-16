namespace SmartKb.Contracts.Configuration;

public sealed class ServiceBusSettings
{
    public const string SectionName = "ServiceBus";

    public string ConnectionString { get; set; } = string.Empty;
    public string QueueName { get; set; } = "ingestion-jobs";
    public int MaxDeliveryCount { get; set; } = 10;
    public int MaxConcurrentCalls { get; set; } = 5;
}
