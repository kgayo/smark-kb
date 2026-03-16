using SmartKb.Contracts.Enums;

namespace SmartKb.Contracts.Models;

/// <summary>
/// Canonical normalized record representing a single source artifact after ingestion.
/// All connectors normalize their output to this schema before chunking and indexing.
/// </summary>
public sealed record CanonicalRecord
{
    public required string TenantId { get; init; }
    public required string EvidenceId { get; init; }
    public required ConnectorType SourceSystem { get; init; }
    public required SourceType SourceType { get; init; }
    public required SourceLocator SourceLocator { get; init; }
    public required string Title { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public required EvidenceStatus Status { get; init; }
    public required string TextContent { get; init; }
    public required RecordPermissions Permissions { get; init; }
    public required string ContentHash { get; init; }

    // Access label derived from permissions, displayed in Evidence Drawer (P0-016).
    public required string AccessLabel { get; init; }

    public string Language { get; init; } = "en-US";
    public IReadOnlyList<string> Tags { get; init; } = [];
    public string? ProductArea { get; init; }
    public string? Severity { get; init; }
    public string? Author { get; init; }
    public IReadOnlyList<string> CustomerRefs { get; init; } = [];
    public string? ParentEvidenceId { get; init; }
    public string? ThreadId { get; init; }
    public IReadOnlyList<string>? PiiFlags { get; init; }
    public string? SensitivityLabel { get; init; }
    public IReadOnlyList<string> ErrorTokens { get; init; } = [];
}
