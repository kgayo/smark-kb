namespace SmartKb.Contracts.Models;

/// <summary>
/// Result of a distillation run — how many candidates were processed and patterns created.
/// </summary>
public sealed record DistillationResult
{
    public int CandidatesEvaluated { get; init; }
    public int PatternsCreated { get; init; }
    public int PatternsSkipped { get; init; }
    public IReadOnlyList<string> CreatedPatternIds { get; init; } = [];
    public IReadOnlyList<string> Errors { get; init; } = [];
    public DateTimeOffset CompletedAt { get; init; }
}

/// <summary>
/// Response DTO for listing distillation candidates.
/// </summary>
public sealed record DistillationCandidateListResponse
{
    public IReadOnlyList<DistillationCandidate> Candidates { get; init; } = [];
    public int TotalCount { get; init; }
}
