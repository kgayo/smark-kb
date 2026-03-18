namespace SmartKb.Data.Entities;

/// <summary>
/// Per-tenant cost optimization settings (P2-003).
/// Controls token budgets, embedding cache behavior, and retrieval compression.
/// One row per tenant; null fields fall back to global defaults.
/// </summary>
public sealed class TenantCostSettingsEntity
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Daily token budget (prompt + completion). Null = unlimited.</summary>
    public long? DailyTokenBudget { get; set; }

    /// <summary>Monthly token budget (prompt + completion). Null = unlimited.</summary>
    public long? MonthlyTokenBudget { get; set; }

    /// <summary>Max prompt tokens per single query (caps evidence + history). Null = use global default.</summary>
    public int? MaxPromptTokensPerQuery { get; set; }

    /// <summary>Max evidence chunks in prompt override. Null = use global default.</summary>
    public int? MaxEvidenceChunksInPrompt { get; set; }

    /// <summary>Enable embedding cache for this tenant. Null = use global default (true).</summary>
    public bool? EnableEmbeddingCache { get; set; }

    /// <summary>Embedding cache TTL in hours. Null = use global default (24).</summary>
    public int? EmbeddingCacheTtlHours { get; set; }

    /// <summary>Enable retrieval compression (truncate long chunks before prompt assembly). Null = use global default.</summary>
    public bool? EnableRetrievalCompression { get; set; }

    /// <summary>Max characters per evidence chunk when compression is enabled. Null = use global default (1500).</summary>
    public int? MaxChunkCharsCompressed { get; set; }

    /// <summary>Alert threshold percentage for daily budget. When usage exceeds this %, emit a warning. Null = use default (80).</summary>
    public int? BudgetAlertThresholdPercent { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public TenantEntity Tenant { get; set; } = null!;
}
