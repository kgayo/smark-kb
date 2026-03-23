namespace SmartKb.Data.Entities;

/// <summary>
/// A per-tenant special token (error code, product identifier) that should be preserved
/// during query preprocessing and optionally boosted in BM25 search.
/// P3-028: Stop-words and special tokens management.
/// </summary>
public sealed class SpecialTokenEntity
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;

    /// <summary>The token value (e.g., "0x80070005", "BSOD", "HTTP 502", "AADSTS50076").</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Category for grouping (e.g., "error-code", "product-name", "status-code").</summary>
    public string Category { get; set; } = "error-code";

    /// <summary>
    /// BM25 boost factor. The token is repeated this many times in the search query
    /// to increase its weight in BM25 ranking. Default 2 = moderate boost.
    /// </summary>
    public int BoostFactor { get; set; } = 2;

    /// <summary>Whether this token is active.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Optional human-readable description.</summary>
    public string? Description { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;

    public TenantEntity Tenant { get; set; } = null!;
}
