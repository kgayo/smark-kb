namespace SmartKb.Contracts.Configuration;

public sealed class BlobStorageSettings
{
    public const string SectionName = "BlobStorage";

    /// <summary>
    /// Blob service URI (e.g. "https://stsmartkbdev.blob.core.windows.net").
    /// When set, Managed Identity (DefaultAzureCredential) is used.
    /// </summary>
    public string ServiceUri { get; set; } = string.Empty;

    /// <summary>
    /// Connection string fallback. Ignored when ServiceUri is set.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Container name for raw ingestion content snapshots.
    /// </summary>
    public string RawContentContainer { get; set; } = "raw-content";

    public bool IsConfigured =>
        !string.IsNullOrEmpty(ServiceUri) || !string.IsNullOrEmpty(ConnectionString);

    public bool UsesManagedIdentity => !string.IsNullOrEmpty(ServiceUri);
}
