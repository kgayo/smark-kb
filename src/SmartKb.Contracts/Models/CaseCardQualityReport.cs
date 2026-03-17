namespace SmartKb.Contracts.Models;

/// <summary>
/// Result of case-card quality validation. Contains an overall score,
/// pass/fail status, and individual quality issue details.
/// </summary>
public sealed record CaseCardQualityReport
{
    /// <summary>Overall quality score (0.0-1.0).</summary>
    public required float QualityScore { get; init; }

    /// <summary>True if the pattern meets the minimum quality threshold.</summary>
    public required bool Passed { get; init; }

    /// <summary>True if the pattern is below the reject threshold and should not be saved.</summary>
    public required bool Rejected { get; init; }

    /// <summary>Individual quality issues found during validation.</summary>
    public required IReadOnlyList<QualityIssue> Issues { get; init; }
}

/// <summary>A single quality issue found during case-card validation.</summary>
public sealed record QualityIssue
{
    /// <summary>The field or aspect that has the issue.</summary>
    public required string Field { get; init; }

    /// <summary>Severity: "error" (fails gate), "warning" (reduces score).</summary>
    public required string Severity { get; init; }

    /// <summary>Human-readable description of the issue.</summary>
    public required string Message { get; init; }

    /// <summary>Score penalty applied for this issue (0.0-1.0).</summary>
    public required float Penalty { get; init; }
}
