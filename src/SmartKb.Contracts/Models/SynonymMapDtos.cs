namespace SmartKb.Contracts.Models;

// --- Request DTOs ---

public sealed record CreateSynonymRuleRequest
{
    public required string Rule { get; init; }
    public string GroupName { get; init; } = "general";
    public string? Description { get; init; }
}

public sealed record UpdateSynonymRuleRequest
{
    public string? Rule { get; init; }
    public string? GroupName { get; init; }
    public string? Description { get; init; }
    public bool? IsActive { get; init; }
}

public sealed record SeedSynonymRulesRequest
{
    public bool OverwriteExisting { get; init; }
}

// --- Response DTOs ---

public sealed record SynonymRuleResponse
{
    public required Guid Id { get; init; }
    public required string TenantId { get; init; }
    public required string GroupName { get; init; }
    public required string Rule { get; init; }
    public string? Description { get; init; }
    public required bool IsActive { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public required string CreatedBy { get; init; }
    public string? UpdatedBy { get; init; }
}

public sealed record SynonymRuleListResponse
{
    public required IReadOnlyList<SynonymRuleResponse> Rules { get; init; }
    public required int TotalCount { get; init; }
    public required IReadOnlyList<string> Groups { get; init; }
}

public sealed record SynonymMapSyncResult
{
    public required bool Success { get; init; }
    public required int RuleCount { get; init; }
    public required string EvidenceSynonymMapName { get; init; }
    public required string PatternSynonymMapName { get; init; }
    public string? ErrorDetail { get; init; }
}

public sealed record SynonymRuleValidationResult
{
    public required bool IsValid { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }

    public static SynonymRuleValidationResult Valid() => new() { IsValid = true, Errors = [] };

    public static SynonymRuleValidationResult Invalid(params string[] errors) =>
        new() { IsValid = false, Errors = errors };
}
