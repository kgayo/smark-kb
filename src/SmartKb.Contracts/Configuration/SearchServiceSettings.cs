namespace SmartKb.Contracts.Configuration;

public sealed class SearchServiceSettings
{
    public const string SectionName = "SearchService";

    /// <summary>
    /// Search service endpoint (e.g. "https://srch-smartkb-dev.search.windows.net").
    /// When set, Managed Identity (DefaultAzureCredential) is used.
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// Admin API key fallback. Ignored when Endpoint is used with Managed Identity.
    /// Only use in local development; production should always use Managed Identity.
    /// </summary>
    public string AdminApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Evidence index name. Defaults to "evidence".
    /// </summary>
    public string EvidenceIndexName { get; set; } = "evidence";

    /// <summary>
    /// Pattern index name. Defaults to "patterns".
    /// </summary>
    public string PatternIndexName { get; set; } = "patterns";

    /// <summary>
    /// Maximum number of documents to send per indexing batch.
    /// </summary>
    public int IndexBatchSize { get; set; } = 100;

    public bool IsConfigured => !string.IsNullOrEmpty(Endpoint);

    public bool UsesManagedIdentity => IsConfigured && string.IsNullOrEmpty(AdminApiKey);
}
