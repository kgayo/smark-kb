namespace SmartKb.Data.Entities;

/// <summary>
/// Tracks right-to-delete (data subject access request) lifecycle.
/// Records which data subject requested deletion and propagation status (P2-001).
/// </summary>
public sealed class DataSubjectDeletionRequestEntity
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;

    /// <summary>The user/subject ID whose data should be deleted.</summary>
    public string SubjectId { get; set; } = string.Empty;

    /// <summary>Who initiated the request (admin user ID).</summary>
    public string RequestedBy { get; set; } = string.Empty;

    /// <summary>Status: Pending, Processing, Completed, Failed.</summary>
    public string Status { get; set; } = "Pending";

    /// <summary>JSON summary of what was deleted: {"sessions":3,"messages":12,"feedbacks":5,...}.</summary>
    public string DeletionSummaryJson { get; set; } = "{}";

    /// <summary>Error details if status is Failed.</summary>
    public string? ErrorDetail { get; set; }

    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public TenantEntity Tenant { get; set; } = null!;
}
