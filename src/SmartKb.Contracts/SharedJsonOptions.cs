using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartKb.Contracts;

/// <summary>
/// Shared, reusable <see cref="JsonSerializerOptions"/> instances.
/// Replaces ~31 duplicate private static fields across Contracts, Data, Api, and Ingestion projects.
/// </summary>
public static class SharedJsonOptions
{
    /// <summary>
    /// camelCase naming, case-insensitive reads, null properties omitted on write.
    /// Used by connector clients and webhook managers that talk to external APIs.
    /// </summary>
    public static readonly JsonSerializerOptions CamelCaseIgnoreNull = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// camelCase naming with case-insensitive reads.
    /// Used by webhook handlers and services that read external JSON payloads.
    /// </summary>
    public static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// camelCase naming only (no case-insensitive reads).
    /// Used by Data repositories for SQL JSON column serialization.
    /// </summary>
    public static readonly JsonSerializerOptions CamelCaseWrite = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// snake_case_lower naming with case-insensitive reads.
    /// Used by OpenAI-facing services (chat orchestrator, classification, summarization).
    /// </summary>
    public static readonly JsonSerializerOptions SnakeCase = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>
    /// Case-insensitive reads only (no naming policy).
    /// Used by services that read varied JSON formats.
    /// </summary>
    public static readonly JsonSerializerOptions CaseInsensitive = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// camelCase naming, compact output (no indentation).
    /// Used by services that serialize JSON for storage or notifications.
    /// </summary>
    public static readonly JsonSerializerOptions CamelCaseCompact = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    /// <summary>
    /// Case-insensitive reads with indented output.
    /// Used by eval baseline serialization for human-readable JSON files.
    /// </summary>
    public static readonly JsonSerializerOptions CaseInsensitiveIndented = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    /// <summary>
    /// camelCase naming with indented output.
    /// Used by eval report serialization for human-readable JSON.
    /// </summary>
    public static readonly JsonSerializerOptions CamelCaseIndented = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
}
