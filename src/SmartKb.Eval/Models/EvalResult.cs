using SmartKb.Contracts.Models;

namespace SmartKb.Eval.Models;

/// <summary>
/// Result of evaluating a single gold dataset case.
/// </summary>
public sealed record EvalResult
{
    public required string CaseId { get; init; }
    public required ChatResponse Response { get; init; }
    public required CaseMetrics Metrics { get; init; }
    public long DurationMs { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// Per-case metric scores.
/// </summary>
public sealed record CaseMetrics
{
    /// <summary>Whether the response type matched expectations.</summary>
    public bool ResponseTypeMatch { get; init; }

    /// <summary>Groundedness score (0-1): proportion of must-include keywords found in answer.</summary>
    public float Groundedness { get; init; }

    /// <summary>Whether citation coverage met expectations (has citations when expected).</summary>
    public bool CitationCoverageMet { get; init; }

    /// <summary>Number of citations in the response.</summary>
    public int CitationCount { get; init; }

    /// <summary>Whether escalation recommendation matched expectations.</summary>
    public bool EscalationMatch { get; init; }

    /// <summary>Whether escalation routing to the correct team matched (if applicable).</summary>
    public bool RoutingMatch { get; init; }

    /// <summary>Whether evidence presence matched expectations.</summary>
    public bool EvidenceMatch { get; init; }

    /// <summary>Proportion of must_include keywords found in the answer (0-1).</summary>
    public float MustIncludeHitRate { get; init; }

    /// <summary>Whether all must_not_include keywords were absent (safety check).</summary>
    public bool SafetyPass { get; init; }

    /// <summary>Whether confidence met the minimum threshold (if specified).</summary>
    public bool ConfidenceMet { get; init; }
}
