using SmartKb.Contracts.Enums;

namespace SmartKb.Data.Entities;

public sealed class OutcomeEventEntity
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public ResolutionType ResolutionType { get; set; }
    public string? TargetTeam { get; set; }
    public bool? Acceptance { get; set; }
    public TimeSpan? TimeToAssign { get; set; }
    public TimeSpan? TimeToResolve { get; set; }
    public string? EscalationTraceId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public SessionEntity Session { get; set; } = null!;
}
