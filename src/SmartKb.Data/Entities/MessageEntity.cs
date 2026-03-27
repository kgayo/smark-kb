using SmartKb.Contracts.Enums;

namespace SmartKb.Data.Entities;

public sealed class MessageEntity
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public MessageRole Role { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? CitationsJson { get; set; }
    public float? Confidence { get; set; }
    public string? ConfidenceLabel { get; set; }
    public string? ConfidenceRationale { get; set; }
    public string? ResponseType { get; set; }
    public string? TraceId { get; set; }
    public string? CorrelationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Unix epoch seconds of <see cref="CreatedAt"/>. Enables server-side filtering in SQLite (which cannot compare DateTimeOffset).</summary>
    public long CreatedAtEpoch { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public SessionEntity Session { get; set; } = null!;
}
