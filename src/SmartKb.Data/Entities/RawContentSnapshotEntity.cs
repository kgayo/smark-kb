namespace SmartKb.Data.Entities;

/// <summary>
/// Tracks raw content blob storage references for ingested evidence records.
/// One snapshot per EvidenceId — provides the stable input for reprocessing
/// with new enrichment versions.
/// </summary>
public sealed class RawContentSnapshotEntity
{
    /// <summary>Unique evidence identifier (same as CanonicalRecord.EvidenceId).</summary>
    public string EvidenceId { get; set; } = string.Empty;

    public string TenantId { get; set; } = string.Empty;
    public Guid ConnectorId { get; set; }

    /// <summary>Blob path relative to the raw-content container.</summary>
    public string BlobPath { get; set; } = string.Empty;

    /// <summary>Content hash of the raw text at time of upload.</summary>
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>Size of the raw content in bytes.</summary>
    public long ContentLength { get; set; }

    /// <summary>MIME content type (e.g. "text/plain; charset=utf-8").</summary>
    public string ContentType { get; set; } = SmartKb.Contracts.CustomMediaTypes.TextPlainUtf8;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // Navigation.
    public ConnectorEntity Connector { get; set; } = null!;
}
