namespace SmartKb.Data.Entities;

/// <summary>
/// A per-tenant synonym rule for Azure AI Search synonym maps.
/// Each rule maps a set of equivalent terms (e.g., "crash, BSOD, blue screen")
/// or one-way mappings ("BSOD => blue screen of death").
/// P3-004: Synonym maps for domain vocabulary.
/// </summary>
public sealed class SynonymMapEntity
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Logical group name (e.g., "error-codes", "product-names", "general").</summary>
    public string GroupName { get; set; } = "general";

    /// <summary>
    /// The synonym rule in Azure AI Search Solr-format syntax.
    /// Equivalent: "crash, BSOD, blue screen"
    /// Explicit: "BSOD => blue screen of death"
    /// </summary>
    public string Rule { get; set; } = string.Empty;

    /// <summary>Optional human-readable description of this rule.</summary>
    public string? Description { get; set; }

    /// <summary>Whether this rule is active and should be included when syncing to Azure Search.</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string? UpdatedBy { get; set; }

    public TenantEntity Tenant { get; set; } = null!;
}
