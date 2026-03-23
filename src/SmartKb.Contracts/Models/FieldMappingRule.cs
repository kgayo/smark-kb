namespace SmartKb.Contracts.Models;

/// <summary>
/// A single field mapping rule from a source system field to a canonical record field.
/// </summary>
public sealed record FieldMappingRule
{
    public required string SourceField { get; init; }
    public required string TargetField { get; init; }
    public FieldTransformType Transform { get; init; } = FieldTransformType.Direct;
    public string? TransformExpression { get; init; }
    public bool IsRequired { get; init; }
    public string? DefaultValue { get; init; }

    /// <summary>
    /// Optional routing tag that this rule produces. When set, the resolved value is assigned
    /// to the specified routing-relevant field on the canonical record (e.g., "product_area").
    /// Applied during normalization before enrichment, so it takes precedence over keyword inference.
    /// </summary>
    public string? RoutingTag { get; init; }
}

/// <summary>
/// Valid routing tag names that can be assigned via field mapping rules.
/// </summary>
public static class RoutingTagNames
{
    public const string ProductArea = "product_area";
    public const string Module = "module";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ProductArea,
        Module,
    };
}

/// <summary>
/// Defines how a source field value is transformed before mapping to the canonical record.
/// </summary>
public enum FieldTransformType
{
    Direct,
    Template,
    Regex,
    Lookup,
    Constant
}

/// <summary>
/// Complete field mapping configuration for a connector, defining how source records
/// map to the canonical schema.
/// </summary>
public sealed record FieldMappingConfig
{
    public IReadOnlyList<FieldMappingRule> Rules { get; init; } = [];
}
