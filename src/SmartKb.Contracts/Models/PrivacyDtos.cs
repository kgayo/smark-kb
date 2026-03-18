namespace SmartKb.Contracts.Models;

/// <summary>
/// PII policy configuration for a tenant (P2-001).
/// </summary>
public sealed record PiiPolicyResponse
{
    public required string TenantId { get; init; }
    public required string EnforcementMode { get; init; }
    public required IReadOnlyList<string> EnabledPiiTypes { get; init; }
    public required IReadOnlyList<CustomPiiPattern> CustomPatterns { get; init; }
    public required bool AuditRedactions { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}

public sealed record PiiPolicyUpdateRequest
{
    /// <summary>Enforcement mode: "redact", "detect", or "disabled".</summary>
    public required string EnforcementMode { get; init; }

    /// <summary>Which built-in PII types to enable: email, phone, ssn, credit_card.</summary>
    public required IReadOnlyList<string> EnabledPiiTypes { get; init; }

    /// <summary>Custom regex patterns to detect/redact.</summary>
    public IReadOnlyList<CustomPiiPattern>? CustomPatterns { get; init; }

    /// <summary>Whether to include redaction details in audit events.</summary>
    public bool AuditRedactions { get; init; } = true;
}

public sealed record CustomPiiPattern
{
    public required string Name { get; init; }
    public required string Pattern { get; init; }
    public required string Placeholder { get; init; }
}

/// <summary>
/// Retention policy configuration for a tenant (P2-001).
/// </summary>
public sealed record RetentionPolicyResponse
{
    public required string TenantId { get; init; }
    public required IReadOnlyList<RetentionPolicyEntry> Policies { get; init; }
}

public sealed record RetentionPolicyEntry
{
    public required string EntityType { get; init; }
    public required int RetentionDays { get; init; }
    public int? MetricRetentionDays { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}

public sealed record RetentionPolicyUpdateRequest
{
    /// <summary>Entity type: AppSession, Message, AuditEvent, EvidenceChunk, AnswerTrace.</summary>
    public required string EntityType { get; init; }

    /// <summary>Number of days before detailed data is eligible for cleanup. Minimum: 1.</summary>
    public required int RetentionDays { get; init; }

    /// <summary>Optional longer window for aggregated metrics. Must be >= RetentionDays when set.</summary>
    public int? MetricRetentionDays { get; init; }
}

/// <summary>
/// Result of a retention cleanup execution.
/// </summary>
public sealed record RetentionCleanupResult
{
    public required string TenantId { get; init; }
    public required string EntityType { get; init; }
    public required int DeletedCount { get; init; }
    public required DateTimeOffset CutoffDate { get; init; }
    public required DateTimeOffset ExecutedAt { get; init; }
}

/// <summary>
/// Data subject deletion request (right-to-delete, P2-001).
/// </summary>
public sealed record DataSubjectDeletionRequest
{
    /// <summary>The user/subject ID whose data should be deleted.</summary>
    public required string SubjectId { get; init; }
}

public sealed record DataSubjectDeletionResponse
{
    public required Guid RequestId { get; init; }
    public required string TenantId { get; init; }
    public required string SubjectId { get; init; }
    public required string Status { get; init; }
    public required DateTimeOffset RequestedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public IReadOnlyDictionary<string, int>? DeletionSummary { get; init; }
    public string? ErrorDetail { get; init; }
}

public sealed record DataSubjectDeletionListResponse
{
    public required IReadOnlyList<DataSubjectDeletionResponse> Requests { get; init; }
    public required int TotalCount { get; init; }
}

/// <summary>
/// Detailed redaction audit record (P2-001).
/// </summary>
public sealed record RedactionAuditDetail
{
    public required string CorrelationId { get; init; }
    public required int ChunksRedacted { get; init; }
    public required IReadOnlyDictionary<string, int> RedactionsByType { get; init; }
    public required IReadOnlyList<string> AffectedChunkIds { get; init; }
    public required string EnforcementMode { get; init; }
}

/// <summary>
/// Retention execution log entry (P2-005).
/// </summary>
public sealed record RetentionExecutionLogEntry
{
    public required Guid Id { get; init; }
    public required string TenantId { get; init; }
    public required string EntityType { get; init; }
    public required int DeletedCount { get; init; }
    public required DateTimeOffset CutoffDate { get; init; }
    public required DateTimeOffset ExecutedAt { get; init; }
    public required long DurationMs { get; init; }
    public required string ActorId { get; init; }
}

/// <summary>
/// Paginated execution history response (P2-005).
/// </summary>
public sealed record RetentionExecutionHistoryResponse
{
    public required IReadOnlyList<RetentionExecutionLogEntry> Entries { get; init; }
    public required int TotalCount { get; init; }
}

/// <summary>
/// Compliance status for a single retention policy (P2-005).
/// </summary>
public sealed record RetentionComplianceEntry
{
    public required string EntityType { get; init; }
    public required int RetentionDays { get; init; }
    public int? MetricRetentionDays { get; init; }
    public DateTimeOffset? LastExecutedAt { get; init; }
    public int? LastDeletedCount { get; init; }
    public required bool IsOverdue { get; init; }
    public required int DaysSinceLastExecution { get; init; }
}

/// <summary>
/// Overall retention compliance report for a tenant (P2-005).
/// </summary>
public sealed record RetentionComplianceReport
{
    public required string TenantId { get; init; }
    public required DateTimeOffset GeneratedAt { get; init; }
    public required bool IsCompliant { get; init; }
    public required int TotalPolicies { get; init; }
    public required int OverduePolicies { get; init; }
    public required IReadOnlyList<RetentionComplianceEntry> Entries { get; init; }
}
