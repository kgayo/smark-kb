namespace SmartKb.Contracts.Models;

/// <summary>
/// Status of a versioned search index instance.
/// </summary>
public static class IndexVersionStatus
{
    public const string Active = "Active";
    public const string Migrating = "Migrating";
    public const string Retired = "Retired";
}

/// <summary>
/// Logical index type identifiers.
/// </summary>
public static class IndexType
{
    public const string Evidence = "evidence";
    public const string Patterns = "patterns";
}

/// <summary>
/// Describes the current version state of a search index.
/// </summary>
public sealed record IndexSchemaVersionInfo(
    Guid Id,
    string IndexType,
    string IndexName,
    int Version,
    string SchemaHash,
    string Status,
    int? DocumentCount,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? RetiredAt);

/// <summary>
/// Describes the result of a schema comparison between current and desired index definitions.
/// </summary>
public sealed record MigrationPlan(
    string IndexType,
    string CurrentIndexName,
    int CurrentVersion,
    string CurrentSchemaHash,
    string DesiredSchemaHash,
    bool MigrationNeeded,
    string NewIndexName,
    int NewVersion);

/// <summary>
/// Result of a migration execution.
/// </summary>
public sealed record MigrationResult(
    bool Success,
    string? Error,
    string IndexType,
    string OldIndexName,
    string NewIndexName,
    int NewVersion,
    int DocumentsReindexed);
