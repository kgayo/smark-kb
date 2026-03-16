namespace SmartKb.Contracts.Models;

/// <summary>
/// Stable identifiers and URLs for a source record, enabling deep linking back to origin.
/// </summary>
public sealed record SourceLocator(
    string ObjectId,
    string Url,
    string? PipelineId = null);
