namespace SmartKb.Contracts.Models;

// --- Seed Request DTOs ---

public sealed record SeedStopWordsRequest
{
    public bool OverwriteExisting { get; init; }
}

public sealed record SeedSpecialTokensRequest
{
    public bool OverwriteExisting { get; init; }
}

// --- Stop Word DTOs ---

public sealed record CreateStopWordRequest
{
    public required string Word { get; init; }
    public string GroupName { get; init; } = "general";
}

public sealed record UpdateStopWordRequest
{
    public string? Word { get; init; }
    public string? GroupName { get; init; }
    public bool? IsActive { get; init; }
}

public sealed record StopWordResponse
{
    public required Guid Id { get; init; }
    public required string TenantId { get; init; }
    public required string Word { get; init; }
    public required string GroupName { get; init; }
    public required bool IsActive { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public required string CreatedBy { get; init; }
}

public sealed record StopWordListResponse
{
    public required IReadOnlyList<StopWordResponse> Words { get; init; }
    public required int TotalCount { get; init; }
    public required IReadOnlyList<string> Groups { get; init; }
}

// --- Special Token DTOs ---

public sealed record CreateSpecialTokenRequest
{
    public required string Token { get; init; }
    public string Category { get; init; } = SpecialTokenDefaults.Category;
    public int BoostFactor { get; init; } = 2;
    public string? Description { get; init; }
}

public sealed record UpdateSpecialTokenRequest
{
    public string? Token { get; init; }
    public string? Category { get; init; }
    public int? BoostFactor { get; init; }
    public bool? IsActive { get; init; }
    public string? Description { get; init; }
}

public sealed record SpecialTokenResponse
{
    public required Guid Id { get; init; }
    public required string TenantId { get; init; }
    public required string Token { get; init; }
    public required string Category { get; init; }
    public required int BoostFactor { get; init; }
    public required bool IsActive { get; init; }
    public string? Description { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public required string CreatedBy { get; init; }
}

public sealed record SpecialTokenListResponse
{
    public required IReadOnlyList<SpecialTokenResponse> Tokens { get; init; }
    public required int TotalCount { get; init; }
    public required IReadOnlyList<string> Categories { get; init; }
}

// --- Validation ---

public sealed record SearchTokenValidationResult
{
    public required bool IsValid { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }

    public static SearchTokenValidationResult Valid() => new() { IsValid = true, Errors = [] };

    public static SearchTokenValidationResult Invalid(params string[] errors) =>
        new() { IsValid = false, Errors = errors };
}

// --- Query Preprocessing Result ---

public sealed record QueryPreprocessingResult
{
    public required string ProcessedQuery { get; init; }
    public required IReadOnlyList<string> RemovedStopWords { get; init; }
    public required IReadOnlyList<string> DetectedSpecialTokens { get; init; }
    public required string BoostedQuery { get; init; }
}
