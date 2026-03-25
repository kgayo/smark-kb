using SmartKb.Contracts.Models;

namespace SmartKb.Data.Entities;

/// <summary>
/// Tracks search index schema versions for blue-green migration and rollback.
/// Each row represents a versioned index instance (e.g., "evidence-v3").
/// P3-005: Search schema versioning and rollback strategy.
/// </summary>
public sealed class IndexSchemaVersionEntity
{
    public Guid Id { get; set; }

    /// <summary>Logical index type: "evidence" or "patterns".</summary>
    public string IndexType { get; set; } = string.Empty;

    /// <summary>Physical index name in Azure AI Search (e.g., "evidence-v3").</summary>
    public string IndexName { get; set; } = string.Empty;

    /// <summary>Monotonically increasing schema version number.</summary>
    public int Version { get; set; }

    /// <summary>SHA-256 hash of the serialized field schema for change detection.</summary>
    public string SchemaHash { get; set; } = string.Empty;

    /// <summary>Current status: Active, Migrating, Retired.</summary>
    public string Status { get; set; } = IndexVersionStatus.Active;

    /// <summary>Total documents indexed during migration (null for initial).</summary>
    public int? DocumentCount { get; set; }

    /// <summary>User or system actor that created this version.</summary>
    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When this version was activated (swapped to live).</summary>
    public DateTimeOffset? ActivatedAt { get; set; }

    /// <summary>When this version was retired (replaced by a newer version).</summary>
    public DateTimeOffset? RetiredAt { get; set; }
}
