using SmartKb.Contracts.Enums;

namespace SmartKb.Contracts.Models;

public sealed record RecordOutcomeRequest
{
    public required ResolutionType ResolutionType { get; init; }
    public string? TargetTeam { get; init; }
    public bool? Acceptance { get; init; }
    public TimeSpan? TimeToAssign { get; init; }
    public TimeSpan? TimeToResolve { get; init; }
    public string? EscalationTraceId { get; init; }
}

public sealed record OutcomeResponse
{
    public required Guid OutcomeId { get; init; }
    public required Guid SessionId { get; init; }
    public required string ResolutionType { get; init; }
    public string? TargetTeam { get; init; }
    public bool? Acceptance { get; init; }
    public string? TimeToAssign { get; init; }
    public string? TimeToResolve { get; init; }
    public string? EscalationTraceId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}

public sealed record OutcomeListResponse
{
    public required Guid SessionId { get; init; }
    public required IReadOnlyList<OutcomeResponse> Outcomes { get; init; }
    public required int TotalCount { get; init; }
}
