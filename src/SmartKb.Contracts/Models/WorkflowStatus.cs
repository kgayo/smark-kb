namespace SmartKb.Contracts.Models;

/// <summary>
/// Shared status constants for workflow entities (maintenance tasks, contradictions,
/// routing recommendations, deletion requests).
/// </summary>
public static class WorkflowStatus
{
    public const string Pending = "Pending";
    public const string Processing = "Processing";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
    public const string Resolved = "Resolved";
    public const string Applied = "Applied";
    public const string Dismissed = "Dismissed";
}

/// <summary>
/// Status constants for external escalation work items.
/// </summary>
public static class EscalationExternalStatus
{
    public const string Pending = "Pending";
    public const string Created = "Created";
    public const string Completed = "Completed";
}
