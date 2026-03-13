using SmartKb.Contracts.Enums;

namespace SmartKb.Data.Entities;

public sealed class SyncRunEntity
{
    public Guid Id { get; set; }
    public Guid ConnectorId { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public SyncRunStatus Status { get; set; }
    public bool IsBackfill { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int RecordsProcessed { get; set; }
    public int RecordsFailed { get; set; }
    public string? Checkpoint { get; set; }
    public string? ErrorDetail { get; set; }
    public string? IdempotencyKey { get; set; }

    public ConnectorEntity Connector { get; set; } = null!;
}
