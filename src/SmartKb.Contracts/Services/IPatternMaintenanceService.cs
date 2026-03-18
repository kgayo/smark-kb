using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

/// <summary>
/// P2-004: Automated pattern maintenance — detects stale, low-quality, and unused patterns.
/// Generates maintenance tasks that require human review before any action is taken.
/// </summary>
public interface IPatternMaintenanceService
{
    /// <summary>
    /// Scans patterns for maintenance issues (stale, low quality, unused).
    /// Creates PatternMaintenanceTask records for new issues found.
    /// </summary>
    Task<MaintenanceDetectionResult> DetectMaintenanceIssuesAsync(
        string tenantId, string actorId, string correlationId);

    /// <summary>Queries maintenance tasks with optional status/type filter and pagination.</summary>
    Task<MaintenanceTaskListResponse> GetMaintenanceTasksAsync(
        string tenantId, string? status, string? taskType, int page, int pageSize);

    /// <summary>Resolves a maintenance task (human review gate — marks as resolved).</summary>
    Task<MaintenanceTaskSummary?> ResolveTaskAsync(
        Guid taskId, string tenantId, string actorId,
        string correlationId, ResolveMaintenanceTaskRequest request);

    /// <summary>Dismisses a maintenance task (human decides no action needed).</summary>
    Task<MaintenanceTaskSummary?> DismissTaskAsync(
        Guid taskId, string tenantId, string actorId,
        string correlationId, ResolveMaintenanceTaskRequest request);
}
