namespace SmartKb.Data.Entities;

/// <summary>
/// Records token usage per chat orchestration call for cost tracking and budget enforcement (P2-003).
/// One row per OrchestrateAsync invocation. Immutable once written.
/// </summary>
public sealed class TokenUsageEntity
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;

    /// <summary>Prompt tokens reported by the OpenAI API (or estimated).</summary>
    public int PromptTokens { get; set; }

    /// <summary>Completion tokens reported by the OpenAI API (or estimated).</summary>
    public int CompletionTokens { get; set; }

    /// <summary>Total tokens (prompt + completion).</summary>
    public int TotalTokens { get; set; }

    /// <summary>Embedding tokens used for query embedding.</summary>
    public int EmbeddingTokens { get; set; }

    /// <summary>Whether embedding was served from cache (P2-003 embedding cache).</summary>
    public bool EmbeddingCacheHit { get; set; }

    /// <summary>Number of evidence chunks included in prompt context.</summary>
    public int EvidenceChunksUsed { get; set; }

    /// <summary>Estimated cost in USD (prompt + completion + embedding rates).</summary>
    public decimal EstimatedCostUsd { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
