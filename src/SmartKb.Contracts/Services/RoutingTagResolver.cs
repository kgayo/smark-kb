using System.Collections.Concurrent;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

/// <summary>
/// Resolves routing tags from field mapping rules and applies them to canonical records.
/// For each rule with a RoutingTag set, reads the value from the CanonicalRecord property
/// named by SourceField, applies the configured transform, and writes the result to the
/// routing-relevant field on the record.
/// </summary>
public sealed class RoutingTagResolver : IRoutingTagResolver
{
    private static readonly ConcurrentDictionary<string, PropertyInfo?> PropertyCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<RoutingTagResolver> _logger;

    public RoutingTagResolver(ILogger<RoutingTagResolver> logger)
    {
        _logger = logger;
    }

    public CanonicalRecord ApplyRoutingTags(CanonicalRecord record, FieldMappingConfig? mapping)
    {
        if (mapping is null) return record;

        var routingRules = mapping.Rules.Where(r => !string.IsNullOrWhiteSpace(r.RoutingTag)).ToList();
        if (routingRules.Count == 0) return record;

        string? productArea = record.ProductArea;
        var tags = record.Tags.ToList();
        var moduleAdded = false;

        foreach (var rule in routingRules)
        {
            var sourceValue = ReadSourceValue(record, rule.SourceField);
            if (sourceValue is null && rule.Transform != FieldTransformType.Constant) continue;

            var resolved = ApplyTransform(sourceValue ?? string.Empty, rule);
            if (string.IsNullOrWhiteSpace(resolved)) continue;

            if (string.Equals(rule.RoutingTag, RoutingTagNames.ProductArea, StringComparison.OrdinalIgnoreCase))
            {
                productArea = resolved;
                _logger.LogDebug(
                    "Routing tag resolved: {SourceField} → product_area = {Value} for {EvidenceId}",
                    rule.SourceField, resolved, record.EvidenceId);
            }
            else if (string.Equals(rule.RoutingTag, RoutingTagNames.Module, StringComparison.OrdinalIgnoreCase))
            {
                if (!moduleAdded)
                {
                    // Add module as a tag with "module:" prefix for downstream routing.
                    var moduleTag = $"module:{resolved}";
                    if (!tags.Contains(moduleTag, StringComparer.OrdinalIgnoreCase))
                        tags.Add(moduleTag);
                    moduleAdded = true;
                    _logger.LogDebug(
                        "Routing tag resolved: {SourceField} → module = {Value} for {EvidenceId}",
                        rule.SourceField, resolved, record.EvidenceId);
                }
            }
            else
            {
                _logger.LogWarning(
                    "Unknown routing tag '{RoutingTag}' on rule {SourceField} → {TargetField}",
                    rule.RoutingTag, rule.SourceField, rule.TargetField);
            }
        }

        // Return updated record only if something changed.
        if (productArea == record.ProductArea && tags.Count == record.Tags.Count)
            return record;

        return record with
        {
            ProductArea = productArea,
            Tags = tags,
        };
    }

    public IReadOnlyList<CanonicalRecord> ApplyRoutingTagsBatch(
        IReadOnlyList<CanonicalRecord> records, FieldMappingConfig? mapping)
    {
        if (mapping is null) return records;

        var routingRules = mapping.Rules.Where(r => !string.IsNullOrWhiteSpace(r.RoutingTag)).ToList();
        if (routingRules.Count == 0) return records;

        var result = new List<CanonicalRecord>(records.Count);
        foreach (var record in records)
            result.Add(ApplyRoutingTags(record, mapping));
        return result;
    }

    internal static string? ReadSourceValue(CanonicalRecord record, string sourceField)
    {
        var prop = PropertyCache.GetOrAdd(sourceField, field =>
            typeof(CanonicalRecord).GetProperty(field,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase));

        if (prop is null) return null;

        var value = prop.GetValue(record);
        return value switch
        {
            string s => s,
            IReadOnlyList<string> list => string.Join(",", list),
            _ => value?.ToString(),
        };
    }

    internal static string? ApplyTransform(string sourceValue, FieldMappingRule rule)
    {
        return rule.Transform switch
        {
            FieldTransformType.Direct => sourceValue,
            FieldTransformType.Constant => rule.DefaultValue,
            FieldTransformType.Lookup => ApplyLookup(sourceValue, rule.TransformExpression),
            FieldTransformType.Regex => ApplyRegex(sourceValue, rule.TransformExpression),
            FieldTransformType.Template => ApplyTemplate(sourceValue, rule.TransformExpression),
            _ => sourceValue,
        };
    }

    private static string? ApplyLookup(string sourceValue, string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) return null;

        // Format: "key1=value1,key2=value2" or "key1=value1;key2=value2"
        // Also supports substring matching: if sourceValue contains the key, it matches.
        var pairs = expression.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var pair in pairs)
        {
            var eqIndex = pair.IndexOf('=');
            if (eqIndex <= 0) continue;
            var key = pair[..eqIndex].Trim();
            var val = pair[(eqIndex + 1)..].Trim();

            if (sourceValue.Contains(key, StringComparison.OrdinalIgnoreCase))
                return val;
        }

        return null;
    }

    internal static string? ApplyRegex(string sourceValue, string? expression, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(expression)) return null;

        try
        {
            var match = Regex.Match(sourceValue, expression, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
            if (match.Success)
                return match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
        }
        catch (RegexMatchTimeoutException ex) { logger?.LogDebug(ex, "Routing tag regex timed out for expression: {Expression}", expression); }
        catch (ArgumentException ex) { logger?.LogDebug(ex, "Routing tag regex invalid for expression: {Expression}", expression); }

        return null;
    }

    private static string? ApplyTemplate(string sourceValue, string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) return null;
        return expression.Replace("{value}", sourceValue, StringComparison.OrdinalIgnoreCase);
    }
}
