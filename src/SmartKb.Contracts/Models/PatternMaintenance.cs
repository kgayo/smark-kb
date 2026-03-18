namespace SmartKb.Contracts.Models;

// --- Contradiction DTOs ---

/// <summary>Summary of a detected contradiction between two patterns.</summary>
public sealed record ContradictionSummary
{
    public required Guid Id { get; init; }
    public required string PatternIdA { get; init; }
    public required string PatternIdB { get; init; }
    public required string PatternTitleA { get; init; }
    public required string PatternTitleB { get; init; }
    public required string ContradictionType { get; init; }
    public float SimilarityScore { get; init; }
    public required string Description { get; init; }
    public IReadOnlyList<string> ConflictingFields { get; init; } = [];
    public required string Status { get; init; }
    public string? Resolution { get; init; }
    public string? ResolvedBy { get; init; }
    public DateTimeOffset? ResolvedAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Paginated response for contradiction queries.</summary>
public sealed record ContradictionListResponse
{
    public IReadOnlyList<ContradictionSummary> Contradictions { get; init; } = [];
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public bool HasMore { get; init; }
}

/// <summary>Result of running contradiction detection.</summary>
public sealed record ContradictionDetectionResult
{
    public int PatternsAnalyzed { get; init; }
    public int ContradictionsFound { get; init; }
    public int NewContradictions { get; init; }
    public int SkippedExisting { get; init; }
    public DateTimeOffset DetectedAt { get; init; }
}

/// <summary>Request to resolve a contradiction.</summary>
public sealed record ResolveContradictionRequest
{
    /// <summary>Resolution type: Merged, Deprecated, Kept, Dismissed.</summary>
    public required string Resolution { get; init; }
    public string? Notes { get; init; }
}

// --- Maintenance Task DTOs ---

/// <summary>Summary of a pattern maintenance task.</summary>
public sealed record MaintenanceTaskSummary
{
    public required Guid Id { get; init; }
    public required string PatternId { get; init; }
    public required string PatternTitle { get; init; }
    public required string TaskType { get; init; }
    public required string Severity { get; init; }
    public required string Description { get; init; }
    public required string RecommendedAction { get; init; }
    public IReadOnlyDictionary<string, object> Metrics { get; init; } = new Dictionary<string, object>();
    public required string Status { get; init; }
    public string? ResolvedBy { get; init; }
    public DateTimeOffset? ResolvedAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Paginated response for maintenance task queries.</summary>
public sealed record MaintenanceTaskListResponse
{
    public IReadOnlyList<MaintenanceTaskSummary> Tasks { get; init; } = [];
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public bool HasMore { get; init; }
}

/// <summary>Result of running maintenance detection scans.</summary>
public sealed record MaintenanceDetectionResult
{
    public int PatternsScanned { get; init; }
    public int TasksCreated { get; init; }
    public int StaleDetected { get; init; }
    public int LowQualityDetected { get; init; }
    public int UnusedDetected { get; init; }
    public DateTimeOffset DetectedAt { get; init; }
}

/// <summary>Request to resolve a maintenance task.</summary>
public sealed record ResolveMaintenanceTaskRequest
{
    public string? Notes { get; init; }
}
