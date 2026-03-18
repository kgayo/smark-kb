using System.Text.Json.Serialization;

namespace SmartKb.Contracts.Models;

/// <summary>
/// Chat request submitted by a support agent.
/// </summary>
public sealed record ChatRequest
{
    /// <summary>The user's natural language query.</summary>
    public required string Query { get; init; }

    /// <summary>Session history for multi-turn context (populated from prior messages).</summary>
    public IReadOnlyList<ChatMessage> SessionHistory { get; init; } = [];

    /// <summary>Optional user group memberships for ACL security trimming.</summary>
    public IReadOnlyList<string>? UserGroups { get; init; }

    /// <summary>Max citations to return (overrides settings default if set).</summary>
    public int? MaxCitations { get; init; }

    /// <summary>Optional retrieval filters to narrow search results by metadata (P1-007).</summary>
    public RetrievalFilter? Filters { get; init; }
}

/// <summary>
/// Structured chat response with grounded answer, citations, confidence, and escalation signal.
/// Schema-validated via OpenAI structured outputs (jtbd-04).
/// </summary>
public sealed record ChatResponse
{
    /// <summary>Response classification: final_answer, next_steps_only, or escalate.</summary>
    public required string ResponseType { get; init; }

    /// <summary>The generated answer or next-step guidance text.</summary>
    public required string Answer { get; init; }

    /// <summary>Citations linking factual claims to source evidence.</summary>
    public required IReadOnlyList<CitationDto> Citations { get; init; }

    /// <summary>Blended confidence score (0.0–1.0). D-003: model self-report + retrieval heuristic.</summary>
    public required float Confidence { get; init; }

    /// <summary>Categorical label: High (>=0.7), Medium (0.4–0.7), Low (&lt;0.4).</summary>
    public required string ConfidenceLabel { get; init; }

    /// <summary>Suggested diagnostic or troubleshooting next steps.</summary>
    public IReadOnlyList<string> NextSteps { get; init; } = [];

    /// <summary>Escalation recommendation when criteria are met.</summary>
    public EscalationSignal? Escalation { get; init; }

    /// <summary>Trace ID linking this response to retrieval + generation audit trail.</summary>
    public required string TraceId { get; init; }

    /// <summary>Whether sufficient evidence was found by retrieval.</summary>
    public required bool HasEvidence { get; init; }

    /// <summary>System prompt version used for this response.</summary>
    public required string SystemPromptVersion { get; init; }

    /// <summary>Number of evidence chunks that had PII redacted before model context assembly (P0-014A).</summary>
    public int PiiRedactedCount { get; init; }

    /// <summary>Issue category from pre-retrieval classification (P3-001, FR-TRIAGE-001).</summary>
    public string? IssueCategory { get; init; }

    /// <summary>Product area from pre-retrieval classification (P3-001, FR-TRIAGE-001).</summary>
    public string? ClassifiedProductArea { get; init; }

    /// <summary>Severity estimate from pre-retrieval classification (P3-001, FR-TRIAGE-001).</summary>
    public string? SeverityGuess { get; init; }

    /// <summary>Classification confidence score (0.0–1.0) from pre-retrieval step (P3-001).</summary>
    public float ClassificationConfidence { get; init; }

    /// <summary>Suggestions for missing information that would improve answer quality (P3-001, FR-TRIAGE-002).</summary>
    public IReadOnlyList<string> MissingInfoSuggestions { get; init; } = [];

    /// <summary>Escalation likelihood from classification (0.0–1.0) (P3-001).</summary>
    public float EscalationLikelihood { get; init; }
}

/// <summary>
/// Citation linking a factual claim to a specific evidence chunk.
/// </summary>
public sealed record CitationDto
{
    public required string ChunkId { get; init; }
    public required string EvidenceId { get; init; }
    public required string Title { get; init; }
    public required string SourceUrl { get; init; }
    public required string SourceSystem { get; init; }
    public required string Snippet { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public required string AccessLabel { get; init; }
}

/// <summary>
/// Escalation recommendation signal. D-004 schema (full routing deferred to P0-015).
/// </summary>
public sealed record EscalationSignal
{
    public required bool Recommended { get; init; }
    public string TargetTeam { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public string HandoffNote { get; init; } = string.Empty;
}

/// <summary>
/// A single message in session history for multi-turn context.
/// </summary>
public sealed record ChatMessage
{
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("content")]
    public required string Content { get; init; }
}

/// <summary>
/// Internal DTO: raw structured output from OpenAI before confidence blending and mapping.
/// </summary>
internal sealed record OpenAiStructuredResponse
{
    [JsonPropertyName("response_type")]
    public string ResponseType { get; init; } = "final_answer";

    [JsonPropertyName("answer")]
    public string Answer { get; init; } = string.Empty;

    [JsonPropertyName("citations")]
    public List<string> Citations { get; init; } = [];

    [JsonPropertyName("confidence")]
    public float Confidence { get; init; }

    [JsonPropertyName("confidence_rationale")]
    public string ConfidenceRationale { get; init; } = string.Empty;

    [JsonPropertyName("next_steps")]
    public List<string> NextSteps { get; init; } = [];

    [JsonPropertyName("escalation")]
    public OpenAiEscalationOutput Escalation { get; init; } = new();
}

/// <summary>
/// Internal DTO: escalation fields from OpenAI structured output.
/// </summary>
internal sealed record OpenAiEscalationOutput
{
    [JsonPropertyName("recommended")]
    public bool Recommended { get; init; }

    [JsonPropertyName("target_team")]
    public string TargetTeam { get; init; } = string.Empty;

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = string.Empty;

    [JsonPropertyName("handoff_note")]
    public string HandoffNote { get; init; } = string.Empty;
}

/// <summary>
/// Result of pre-retrieval query classification (P3-001, FR-TRIAGE-001, FR-TRIAGE-002).
/// Used to bias retrieval filters and populate triage metadata in ChatResponse.
/// </summary>
public sealed record ClassificationResult
{
    /// <summary>Primary issue category (e.g., Authentication, Billing, Integration, Performance).</summary>
    [JsonPropertyName("issue_category")]
    public string IssueCategory { get; init; } = string.Empty;

    /// <summary>Product/module area (e.g., SSO, Payment, API, Dashboard).</summary>
    [JsonPropertyName("product_area")]
    public string ProductArea { get; init; } = string.Empty;

    /// <summary>Estimated severity: P1 (critical), P2 (significant), P3 (minor), P4 (cosmetic), Unknown.</summary>
    [JsonPropertyName("severity_hint")]
    public string SeverityHint { get; init; } = "Unknown";

    /// <summary>Whether customer-specific context is needed for accurate diagnosis.</summary>
    [JsonPropertyName("needs_customer_lookup")]
    public bool NeedsCustomerLookup { get; init; }

    /// <summary>Suggestions for missing information that would improve answer confidence (FR-TRIAGE-002).</summary>
    [JsonPropertyName("missing_info_suggestions")]
    public List<string> MissingInfoSuggestions { get; init; } = [];

    /// <summary>Confidence in this classification (0.0–1.0).</summary>
    [JsonPropertyName("classification_confidence")]
    public float ClassificationConfidence { get; init; }

    /// <summary>Escalation likelihood score (0.0–1.0).</summary>
    [JsonPropertyName("escalation_likelihood")]
    public float EscalationLikelihood { get; init; }

    /// <summary>Suggested source type preferences for retrieval (e.g., "ticket", "wiki_page").</summary>
    [JsonPropertyName("source_type_preference")]
    public List<string> SourceTypePreference { get; init; } = [];

    /// <summary>Suggested time horizon in days for retrieval filtering.</summary>
    [JsonPropertyName("time_horizon_days")]
    public int? TimeHorizonDays { get; init; }

    /// <summary>Returns a default/empty classification for fallback scenarios.</summary>
    public static ClassificationResult Empty => new();
}
