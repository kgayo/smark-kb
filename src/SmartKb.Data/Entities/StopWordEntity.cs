namespace SmartKb.Data.Entities;

/// <summary>
/// A per-tenant stop word that should be stripped from search queries before BM25 matching.
/// P3-028: Stop-words and special tokens management.
/// </summary>
public sealed class StopWordEntity
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;

    /// <summary>The word to suppress from search queries (case-insensitive).</summary>
    public string Word { get; set; } = string.Empty;

    /// <summary>Logical group name (e.g., "general", "greeting", "filler").</summary>
    public string GroupName { get; set; } = "general";

    /// <summary>Whether this stop word is active and should be applied during query preprocessing.</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;

    public TenantEntity Tenant { get; set; } = null!;
}
