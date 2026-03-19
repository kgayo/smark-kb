using SmartKb.Contracts.Enums;

namespace SmartKb.Contracts.Models;

/// <summary>
/// Domain model for a distilled case pattern — a structured playbook derived from
/// solved tickets. Indexed in the Pattern Store (Azure AI Search) for retrieval fusion
/// with the Evidence Store.
/// </summary>
public sealed record CasePattern
{
    /// <summary>Unique pattern identifier (format: pattern-{guid}).</summary>
    public required string PatternId { get; init; }

    public required string TenantId { get; init; }

    public required string Title { get; init; }

    /// <summary>Concise description of the problem this pattern addresses.</summary>
    public required string ProblemStatement { get; init; }

    /// <summary>Identified root cause of the problem, distinct from the problem statement.</summary>
    public string? RootCause { get; init; }

    /// <summary>Observable symptoms that indicate this pattern applies.</summary>
    public IReadOnlyList<string> Symptoms { get; init; } = [];

    /// <summary>Ordered steps to diagnose the root cause.</summary>
    public IReadOnlyList<string> DiagnosisSteps { get; init; } = [];

    /// <summary>Ordered steps to resolve the issue.</summary>
    public IReadOnlyList<string> ResolutionSteps { get; init; } = [];

    /// <summary>Ordered steps to verify the fix.</summary>
    public IReadOnlyList<string> VerificationSteps { get; init; } = [];

    /// <summary>Optional workaround if full resolution is not immediately possible.</summary>
    public string? Workaround { get; init; }

    /// <summary>Criteria that should trigger escalation instead of self-resolution.</summary>
    public IReadOnlyList<string> EscalationCriteria { get; init; } = [];

    /// <summary>Target team for escalation.</summary>
    public string? EscalationTargetTeam { get; init; }

    /// <summary>Evidence record IDs that justify this pattern (min 1 required).</summary>
    public required IReadOnlyList<string> RelatedEvidenceIds { get; init; }

    /// <summary>Pattern confidence score (0.0 - 1.0).</summary>
    public float Confidence { get; init; }

    /// <summary>Governance trust level: draft → reviewed → approved → deprecated.</summary>
    public required TrustLevel TrustLevel { get; init; }

    /// <summary>Pattern version (incremented on each update).</summary>
    public int Version { get; init; } = 1;

    /// <summary>Pattern ID this one supersedes, if any.</summary>
    public string? SupersedesPatternId { get; init; }

    /// <summary>Constraints limiting when this pattern is applicable.</summary>
    public IReadOnlyList<string> ApplicabilityConstraints { get; init; } = [];

    /// <summary>Conditions that exclude this pattern from applying.</summary>
    public IReadOnlyList<string> Exclusions { get; init; } = [];

    /// <summary>Product area for routing and filtering.</summary>
    public string? ProductArea { get; init; }

    /// <summary>Tags for faceted search.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Vector embedding for semantic retrieval (1536 dims).</summary>
    public float[]? EmbeddingVector { get; init; }

    // ACL fields
    public AccessVisibility Visibility { get; init; } = AccessVisibility.Internal;
    public IReadOnlyList<string> AllowedGroups { get; init; } = [];
    public string AccessLabel { get; init; } = "Internal";

    /// <summary>Deep link URL for viewing the pattern.</summary>
    public string SourceUrl { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
