using System.Text.Json.Serialization;

namespace SmartKb.Contracts.Models;

public sealed record CreateSessionRequest
{
    public string? Title { get; init; }
    public string? CustomerRef { get; init; }
}

public sealed record SessionResponse
{
    public required Guid SessionId { get; init; }
    public required string TenantId { get; init; }
    public required string UserId { get; init; }
    public string? Title { get; init; }
    public string? CustomerRef { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public required int MessageCount { get; init; }
}

public sealed record SessionListResponse
{
    public required IReadOnlyList<SessionResponse> Sessions { get; init; }
    public required int TotalCount { get; init; }
}

public sealed record SendMessageRequest
{
    public required string Query { get; init; }
    public IReadOnlyList<string>? UserGroups { get; init; }
    public int? MaxCitations { get; init; }

    /// <summary>Optional retrieval filters to narrow search results by metadata (P1-007).</summary>
    public RetrievalFilter? Filters { get; init; }
}

public sealed record MessageResponse
{
    public required Guid MessageId { get; init; }
    public required Guid SessionId { get; init; }
    public required string Role { get; init; }
    public required string Content { get; init; }
    public IReadOnlyList<CitationDto>? Citations { get; init; }
    public float? Confidence { get; init; }
    public string? ConfidenceLabel { get; init; }
    public string? ConfidenceRationale { get; init; }
    public string? ResponseType { get; init; }
    public string? TraceId { get; init; }
    public string? CorrelationId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}

public sealed record MessageListResponse
{
    public required Guid SessionId { get; init; }
    public required IReadOnlyList<MessageResponse> Messages { get; init; }
    public required int TotalCount { get; init; }
}

public sealed record SessionChatResponse
{
    public required SessionResponse Session { get; init; }
    public required MessageResponse UserMessage { get; init; }
    public required MessageResponse AssistantMessage { get; init; }
    public required ChatResponse ChatResponse { get; init; }
}
