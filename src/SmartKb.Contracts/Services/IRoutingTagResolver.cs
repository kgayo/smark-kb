using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

/// <summary>
/// Resolves routing tags from field mapping rules and applies them to canonical records.
/// Processes rules where <see cref="FieldMappingRule.RoutingTag"/> is set, reads the
/// source value from the canonical record, applies the configured transform, and writes
/// the result to the appropriate routing-relevant field (e.g., ProductArea).
/// </summary>
public interface IRoutingTagResolver
{
    /// <summary>
    /// Applies routing tag rules from the field mapping config to a canonical record,
    /// returning a new record with routing fields populated.
    /// </summary>
    CanonicalRecord ApplyRoutingTags(CanonicalRecord record, FieldMappingConfig? mapping);

    /// <summary>
    /// Applies routing tag rules to a batch of canonical records.
    /// </summary>
    IReadOnlyList<CanonicalRecord> ApplyRoutingTagsBatch(
        IReadOnlyList<CanonicalRecord> records, FieldMappingConfig? mapping);
}
