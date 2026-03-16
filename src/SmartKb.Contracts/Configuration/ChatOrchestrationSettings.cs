namespace SmartKb.Contracts.Configuration;

/// <summary>
/// Chat orchestration configuration. Resolves design decisions:
/// D-003: Confidence scoring — 0-1 float, model self-report + retrieval heuristic blend.
/// D-006: OpenAI model — gpt-4o (latest), configurable via OpenAiSettings.Model.
/// D-010: Session token budget — sliding window, 80% of context window, drop oldest first.
/// D-013: Hallucination degradation — refuse + next-steps when confidence &lt; 0.3 and no evidence.
/// </summary>
public sealed class ChatOrchestrationSettings
{
    public const string SectionName = "ChatOrchestration";

    /// <summary>Max token budget for the full prompt (system + context + history + query). 80% of gpt-4o 128k.</summary>
    public int MaxTokenBudget { get; set; } = 102_400;

    /// <summary>Max tokens reserved for the model response.</summary>
    public int MaxResponseTokens { get; set; } = 4096;

    /// <summary>Approximate tokens reserved for the system prompt template.</summary>
    public int SystemPromptTokenReserve { get; set; } = 1500;

    /// <summary>Weight of model self-reported confidence in blended score (D-003).</summary>
    public float ModelConfidenceWeight { get; set; } = 0.6f;

    /// <summary>Weight of retrieval heuristic in blended score (D-003).</summary>
    public float RetrievalConfidenceWeight { get; set; } = 0.4f;

    /// <summary>Confidence >= this threshold is labeled High (D-003).</summary>
    public float HighConfidenceThreshold { get; set; } = 0.7f;

    /// <summary>Confidence >= this threshold (but below High) is labeled Medium (D-003).</summary>
    public float MediumConfidenceThreshold { get; set; } = 0.4f;

    /// <summary>Confidence below this triggers refuse + next-steps degradation (D-013).</summary>
    public float DegradationThreshold { get; set; } = 0.3f;

    /// <summary>Max number of evidence chunks to include in the prompt context.</summary>
    public int MaxEvidenceChunksInPrompt { get; set; } = 15;

    /// <summary>Max citations to return in the response.</summary>
    public int MaxCitations { get; set; } = 10;

    /// <summary>System prompt template version for tracking.</summary>
    public string SystemPromptVersion { get; set; } = "1.0";

    public string GetConfidenceLabel(float confidence)
    {
        if (confidence >= HighConfidenceThreshold) return "High";
        if (confidence >= MediumConfidenceThreshold) return "Medium";
        return "Low";
    }
}
