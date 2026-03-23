namespace SmartKb.Contracts.Models;

/// <summary>Summary DTO for gold case list view (P3-022).</summary>
public sealed record GoldCaseSummary
{
    public required Guid Id { get; init; }
    public required string CaseId { get; init; }
    public required string Query { get; init; }
    public required string ResponseType { get; init; }
    public required IReadOnlyList<string> Tags { get; init; }
    public Guid? SourceFeedbackId { get; init; }
    public required string CreatedBy { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>Full gold case detail (P3-022).</summary>
public sealed record GoldCaseDetail
{
    public required Guid Id { get; init; }
    public required string CaseId { get; init; }
    public required string Query { get; init; }
    public GoldCaseContext? Context { get; init; }
    public required GoldCaseExpected Expected { get; init; }
    public required IReadOnlyList<string> Tags { get; init; }
    public Guid? SourceFeedbackId { get; init; }
    public required string CreatedBy { get; init; }
    public string? UpdatedBy { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>Context for a gold case.</summary>
public sealed record GoldCaseContext
{
    public IReadOnlyList<string>? CustomerRefs { get; init; }
    public string? ProductAreaHint { get; init; }
    public Dictionary<string, string>? Environment { get; init; }
    public IReadOnlyList<GoldCaseSessionMessage>? SessionHistory { get; init; }
}

public sealed record GoldCaseSessionMessage
{
    public required string Role { get; init; }
    public required string Content { get; init; }
}

/// <summary>Expected evaluation criteria for a gold case.</summary>
public sealed record GoldCaseExpected
{
    public required string ResponseType { get; init; }
    public IReadOnlyList<string>? MustInclude { get; init; }
    public IReadOnlyList<string>? MustNotInclude { get; init; }
    public bool? MustCiteSources { get; init; }
    public int? MinCitations { get; init; }
    public GoldCaseExpectedEscalation? ExpectedEscalation { get; init; }
    public float? MinConfidence { get; init; }
    public bool? ShouldHaveEvidence { get; init; }
}

public sealed record GoldCaseExpectedEscalation
{
    public required bool Recommended { get; init; }
    public string? TargetTeam { get; init; }
}

/// <summary>Paginated gold case list response.</summary>
public sealed record GoldCaseListResponse
{
    public required IReadOnlyList<GoldCaseSummary> Cases { get; init; }
    public required int TotalCount { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required bool HasMore { get; init; }
}

/// <summary>Request to create a gold case.</summary>
public sealed record CreateGoldCaseRequest
{
    public required string CaseId { get; init; }
    public required string Query { get; init; }
    public GoldCaseContext? Context { get; init; }
    public required GoldCaseExpected Expected { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
}

/// <summary>Request to update a gold case.</summary>
public sealed record UpdateGoldCaseRequest
{
    public string? Query { get; init; }
    public GoldCaseContext? Context { get; init; }
    public GoldCaseExpected? Expected { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
}

/// <summary>Request to promote a feedback entry to a gold case.</summary>
public sealed record PromoteFromFeedbackRequest
{
    public required Guid FeedbackId { get; init; }
    public required string CaseId { get; init; }
    public required GoldCaseExpected Expected { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
}
