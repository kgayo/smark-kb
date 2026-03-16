using SmartKb.Contracts.Enums;

namespace SmartKb.Contracts.Models;

public sealed record SubmitFeedbackRequest
{
    public required FeedbackType Type { get; init; }
    public IReadOnlyList<FeedbackReasonCode> ReasonCodes { get; init; } = [];
    public string? Comment { get; init; }
    public string? CorrectionText { get; init; }
    public string? CorrectedAnswer { get; init; }
}

public sealed record FeedbackResponse
{
    public required Guid FeedbackId { get; init; }
    public required Guid MessageId { get; init; }
    public required Guid SessionId { get; init; }
    public required string Type { get; init; }
    public required IReadOnlyList<string> ReasonCodes { get; init; }
    public string? Comment { get; init; }
    public string? CorrectionText { get; init; }
    public string? CorrectedAnswer { get; init; }
    public string? TraceId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}

public sealed record FeedbackListResponse
{
    public required Guid MessageId { get; init; }
    public required IReadOnlyList<FeedbackResponse> Feedbacks { get; init; }
    public required int TotalCount { get; init; }
}
