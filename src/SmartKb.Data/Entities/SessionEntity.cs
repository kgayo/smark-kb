namespace SmartKb.Data.Entities;

public sealed class SessionEntity
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? CustomerRef { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public TenantEntity Tenant { get; set; } = null!;
    public ICollection<MessageEntity> Messages { get; set; } = [];
    public ICollection<FeedbackEntity> Feedbacks { get; set; } = [];
    public ICollection<OutcomeEventEntity> OutcomeEvents { get; set; } = [];
}
