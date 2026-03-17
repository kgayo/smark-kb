namespace SmartKb.Contracts.Models;

public sealed record AuditEventQueryRequest
{
    public string? EventType { get; init; }
    public string? ActorId { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public string? CorrelationId { get; init; }
    public int PageSize { get; init; } = 50;
    public int Page { get; init; } = 1;
}

public sealed record AuditEventResponse
{
    public required string EventId { get; init; }
    public required string EventType { get; init; }
    public required string TenantId { get; init; }
    public required string ActorId { get; init; }
    public required string CorrelationId { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required string Detail { get; init; }
}

public sealed record AuditEventListResponse
{
    public required IReadOnlyList<AuditEventResponse> Events { get; init; }
    public required int TotalCount { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required bool HasMore { get; init; }
}

public sealed record AuditExportCursor
{
    public DateTimeOffset? AfterTimestamp { get; init; }
    public Guid? AfterId { get; init; }
    public string? EventType { get; init; }
    public string? ActorId { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public int Limit { get; init; } = 1000;
}
