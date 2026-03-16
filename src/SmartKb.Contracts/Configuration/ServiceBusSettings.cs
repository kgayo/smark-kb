namespace SmartKb.Contracts.Configuration;

public sealed class ServiceBusSettings
{
    public const string SectionName = "ServiceBus";

    /// <summary>
    /// Fully qualified namespace (e.g. "sb-smartkb-dev.servicebus.windows.net").
    /// When set, Managed Identity (DefaultAzureCredential) is used instead of connection string.
    /// </summary>
    public string FullyQualifiedNamespace { get; set; } = string.Empty;

    /// <summary>
    /// Connection string fallback. Ignored when FullyQualifiedNamespace is set.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    public string QueueName { get; set; } = "ingestion-jobs";
    public int MaxDeliveryCount { get; set; } = 10;
    public int MaxConcurrentCalls { get; set; } = 5;

    public bool IsConfigured =>
        !string.IsNullOrEmpty(FullyQualifiedNamespace) || !string.IsNullOrEmpty(ConnectionString);

    public bool UsesManagedIdentity => !string.IsNullOrEmpty(FullyQualifiedNamespace);
}
