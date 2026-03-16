using SmartKb.Contracts.Enums;

namespace SmartKb.Data.Entities;

public sealed class FeedbackEntity
{
    public Guid Id { get; set; }
    public Guid MessageId { get; set; }
    public Guid SessionId { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public FeedbackType Type { get; set; }
    public string? ReasonCodesJson { get; set; }
    public string? Comment { get; set; }
    public string? CorrectionText { get; set; }
    public string? CorrectedAnswer { get; set; }
    public string? TraceId { get; set; }
    public string? CorrelationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public SessionEntity Session { get; set; } = null!;
    public MessageEntity Message { get; set; } = null!;
}
