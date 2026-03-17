using System.Text.Json.Serialization;

namespace SmartKb.Eval.Models;

/// <summary>
/// A single gold dataset evaluation case (D-007 schema).
/// </summary>
public sealed record EvalCase
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("tenant_id")]
    public required string TenantId { get; init; }

    [JsonPropertyName("query")]
    public required string Query { get; init; }

    [JsonPropertyName("context")]
    public EvalContext? Context { get; init; }

    [JsonPropertyName("expected")]
    public required EvalExpected Expected { get; init; }

    [JsonPropertyName("tags")]
    public IReadOnlyList<string> Tags { get; init; } = [];
}

public sealed record EvalContext
{
    [JsonPropertyName("customer_refs")]
    public IReadOnlyList<string>? CustomerRefs { get; init; }

    [JsonPropertyName("product_area_hint")]
    public string? ProductAreaHint { get; init; }

    [JsonPropertyName("environment")]
    public Dictionary<string, string>? Environment { get; init; }

    [JsonPropertyName("session_history")]
    public IReadOnlyList<EvalSessionMessage>? SessionHistory { get; init; }
}

public sealed record EvalSessionMessage
{
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("content")]
    public required string Content { get; init; }
}

public sealed record EvalExpected
{
    [JsonPropertyName("response_type")]
    public required string ResponseType { get; init; }

    [JsonPropertyName("must_include")]
    public IReadOnlyList<string>? MustInclude { get; init; }

    [JsonPropertyName("must_not_include")]
    public IReadOnlyList<string>? MustNotInclude { get; init; }

    [JsonPropertyName("must_cite_sources")]
    public bool? MustCiteSources { get; init; }

    [JsonPropertyName("min_citations")]
    public int? MinCitations { get; init; }

    [JsonPropertyName("expected_escalation")]
    public EvalExpectedEscalation? ExpectedEscalation { get; init; }

    [JsonPropertyName("min_confidence")]
    public float? MinConfidence { get; init; }

    [JsonPropertyName("should_have_evidence")]
    public bool? ShouldHaveEvidence { get; init; }
}

public sealed record EvalExpectedEscalation
{
    [JsonPropertyName("recommended")]
    public required bool Recommended { get; init; }

    [JsonPropertyName("target_team")]
    public string? TargetTeam { get; init; }
}
