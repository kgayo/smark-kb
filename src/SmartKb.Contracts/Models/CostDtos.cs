namespace SmartKb.Contracts.Models;

/// <summary>Response DTO for per-tenant cost settings (P2-003).</summary>
public sealed record CostSettingsResponse
{
    public required string TenantId { get; init; }
    public required long? DailyTokenBudget { get; init; }
    public required long? MonthlyTokenBudget { get; init; }
    public required int? MaxPromptTokensPerQuery { get; init; }
    public required int MaxEvidenceChunksInPrompt { get; init; }
    public required bool EnableEmbeddingCache { get; init; }
    public required int EmbeddingCacheTtlHours { get; init; }
    public required bool EnableRetrievalCompression { get; init; }
    public required int MaxChunkCharsCompressed { get; init; }
    public required int BudgetAlertThresholdPercent { get; init; }
    public required bool HasOverrides { get; init; }
}

/// <summary>Request DTO for updating per-tenant cost settings. Null fields = keep current.</summary>
public sealed record UpdateCostSettingsRequest
{
    public long? DailyTokenBudget { get; init; }
    public long? MonthlyTokenBudget { get; init; }
    public int? MaxPromptTokensPerQuery { get; init; }
    public int? MaxEvidenceChunksInPrompt { get; init; }
    public bool? EnableEmbeddingCache { get; init; }
    public int? EmbeddingCacheTtlHours { get; init; }
    public bool? EnableRetrievalCompression { get; init; }
    public int? MaxChunkCharsCompressed { get; init; }
    public int? BudgetAlertThresholdPercent { get; init; }
}

/// <summary>Token usage summary for a tenant over a time period.</summary>
public sealed record TokenUsageSummary
{
    public required string TenantId { get; init; }
    public required DateTimeOffset PeriodStart { get; init; }
    public required DateTimeOffset PeriodEnd { get; init; }
    public required long TotalPromptTokens { get; init; }
    public required long TotalCompletionTokens { get; init; }
    public required long TotalTokens { get; init; }
    public required long TotalEmbeddingTokens { get; init; }
    public required int TotalRequests { get; init; }
    public required int EmbeddingCacheHits { get; init; }
    public required int EmbeddingCacheMisses { get; init; }
    public required decimal TotalEstimatedCostUsd { get; init; }
    public required long? DailyTokenBudget { get; init; }
    public required long? MonthlyTokenBudget { get; init; }
    public required float DailyBudgetUtilizationPercent { get; init; }
    public required float MonthlyBudgetUtilizationPercent { get; init; }
}

/// <summary>Daily usage breakdown for trend reporting.</summary>
public sealed record DailyUsageBreakdown
{
    public required DateOnly Date { get; init; }
    public required long PromptTokens { get; init; }
    public required long CompletionTokens { get; init; }
    public required long TotalTokens { get; init; }
    public required long EmbeddingTokens { get; init; }
    public required int RequestCount { get; init; }
    public required int CacheHits { get; init; }
    public required decimal EstimatedCostUsd { get; init; }
}

/// <summary>Token usage record from a single orchestration call.</summary>
public sealed record TokenUsageRecord
{
    public required int PromptTokens { get; init; }
    public required int CompletionTokens { get; init; }
    public required int TotalTokens { get; init; }
    public required int EmbeddingTokens { get; init; }
    public required bool EmbeddingCacheHit { get; init; }
    public required int EvidenceChunksUsed { get; init; }
    public required decimal EstimatedCostUsd { get; init; }
}

/// <summary>Budget check result returned before query execution.</summary>
public sealed record BudgetCheckResult
{
    public required bool Allowed { get; init; }
    public required string? DenialReason { get; init; }
    public required float DailyUtilizationPercent { get; init; }
    public required float MonthlyUtilizationPercent { get; init; }
    public required bool BudgetWarning { get; init; }
    public required string? WarningMessage { get; init; }
}
