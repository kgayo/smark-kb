namespace SmartKb.Contracts.Configuration;

/// <summary>
/// Global cost optimization defaults (P2-003). Per-tenant overrides stored in TenantCostSettings.
/// </summary>
public sealed class CostOptimizationSettings
{
    public const string SectionName = "CostOptimization";

    /// <summary>Enable embedding cache globally. Default true.</summary>
    public bool EnableEmbeddingCache { get; set; } = true;

    /// <summary>Embedding cache TTL in hours. Default 24.</summary>
    public int EmbeddingCacheTtlHours { get; set; } = 24;

    /// <summary>Enable retrieval compression (truncate long evidence chunks). Default false.</summary>
    public bool EnableRetrievalCompression { get; set; }

    /// <summary>Max characters per evidence chunk when compression is enabled. Default 1500.</summary>
    public int MaxChunkCharsCompressed { get; set; } = 1500;

    /// <summary>Alert threshold percentage for daily budget. Default 80.</summary>
    public int BudgetAlertThresholdPercent { get; set; } = 80;

    /// <summary>Cost per 1M prompt tokens in USD (gpt-4o pricing). Default $2.50.</summary>
    public decimal PromptTokenCostPerMillion { get; set; } = 2.50m;

    /// <summary>Cost per 1M completion tokens in USD (gpt-4o pricing). Default $10.00.</summary>
    public decimal CompletionTokenCostPerMillion { get; set; } = 10.00m;

    /// <summary>Cost per 1M embedding tokens in USD (text-embedding-3-large pricing). Default $0.13.</summary>
    public decimal EmbeddingTokenCostPerMillion { get; set; } = 0.13m;
}
