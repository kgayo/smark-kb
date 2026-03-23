using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;

namespace SmartKb.Contracts.Connectors;

/// <summary>
/// HubSpot connector client. Ingests CRM objects (tickets, contacts, companies, deals)
/// via the HubSpot CRM v3 REST API. Supports PAT/OAuth authentication.
/// Implements checkpoint-based incremental sync via lastModifiedDate filter.
/// </summary>
public sealed class HubSpotConnectorClient : IConnectorClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly HashSet<string> SupportedObjectTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "tickets", "contacts", "companies", "deals",
    };

    /// <summary>
    /// Default properties to request per object type when no custom properties are configured.
    /// </summary>
    private static readonly Dictionary<string, string[]> DefaultProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        ["tickets"] = ["subject", "content", "hs_pipeline", "hs_pipeline_stage", "hs_ticket_priority", "hs_ticket_category", "createdate", "hs_lastmodifieddate", "hubspot_owner_id"],
        ["contacts"] = ["firstname", "lastname", "email", "company", "jobtitle", "lifecyclestage", "createdate", "lastmodifieddate", "hubspot_owner_id"],
        ["companies"] = ["name", "description", "industry", "domain", "createdate", "hs_lastmodifieddate", "hubspot_owner_id"],
        ["deals"] = ["dealname", "description", "dealstage", "pipeline", "amount", "closedate", "createdate", "hs_lastmodifieddate", "hubspot_owner_id"],
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HubSpotConnectorClient> _logger;

    public HubSpotConnectorClient(
        IHttpClientFactory httpClientFactory,
        ILogger<HubSpotConnectorClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public ConnectorType Type => ConnectorType.HubSpot;

    public async Task<TestConnectionResponse> TestConnectionAsync(
        string tenantId, string? sourceConfig, string? secretValue,
        CancellationToken cancellationToken = default)
    {
        var config = ParseSourceConfig(sourceConfig, _logger);
        if (config is null)
            return new TestConnectionResponse { Success = false, Message = "Invalid or missing source configuration." };

        if (string.IsNullOrEmpty(secretValue))
            return new TestConnectionResponse { Success = false, Message = "No credentials provided. A HubSpot API key or access token is required." };

        try
        {
            using var client = CreateHttpClient(config.BaseUrl, secretValue);
            // Use the account info endpoint to validate credentials.
            var response = await client.GetAsync("account-info/v3/details", cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return new TestConnectionResponse
                {
                    Success = true,
                    Message = $"Successfully connected to HubSpot portal {config.PortalId}.",
                };
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return new TestConnectionResponse
            {
                Success = false,
                Message = $"Connection failed with status {(int)response.StatusCode}.",
                DiagnosticDetail = body.Length > 500 ? body[..500] : body,
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new TestConnectionResponse
            {
                Success = false,
                Message = "Failed to connect to HubSpot.",
                DiagnosticDetail = ex.Message,
            };
        }
    }

    public async Task<IReadOnlyList<CanonicalRecord>> PreviewAsync(
        string tenantId, string? sourceConfig, FieldMappingConfig? fieldMapping,
        string? secretValue, int sampleSize,
        CancellationToken cancellationToken = default)
    {
        var config = ParseSourceConfig(sourceConfig, _logger);
        if (config is null || string.IsNullOrEmpty(secretValue))
            return [];

        try
        {
            using var client = CreateHttpClient(config.BaseUrl, secretValue);
            var records = new List<CanonicalRecord>();

            foreach (var objectType in ResolveObjectTypes(config))
            {
                if (records.Count >= sampleSize) break;

                var batch = await FetchObjectsAsync(
                    client, config, objectType, tenantId,
                    after: null, lastModified: null,
                    limit: sampleSize - records.Count,
                    cancellationToken);
                records.AddRange(batch.Records);
            }

            return records.Take(sampleSize).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Preview failed for HubSpot connector (tenant={TenantId})", tenantId);
            return [];
        }
    }

    public async Task<FetchResult> FetchAsync(
        string tenantId, string? sourceConfig, FieldMappingConfig? fieldMapping,
        string? secretValue, string? checkpoint, bool isBackfill,
        CancellationToken cancellationToken = default)
    {
        var config = ParseSourceConfig(sourceConfig, _logger);
        if (config is null)
            return ErrorResult("Invalid or missing source configuration.");

        if (string.IsNullOrEmpty(secretValue))
            return ErrorResult("No credentials provided.");

        using var client = CreateHttpClient(config.BaseUrl, secretValue);

        var parsedCheckpoint = HubSpotCheckpoint.Parse(checkpoint);
        var objectTypes = ResolveObjectTypes(config);
        if (objectTypes.Count == 0)
            return ErrorResult("No valid object types configured.");

        var records = new List<CanonicalRecord>();
        var errors = new List<string>();
        var failedRecords = 0;

        // Resume from checkpoint position.
        var startTypeIndex = parsedCheckpoint?.ObjectTypeIndex ?? 0;
        var afterCursor = parsedCheckpoint?.AfterCursor;
        var lastModified = isBackfill ? null : parsedCheckpoint?.LastModified;

        for (var ti = startTypeIndex; ti < objectTypes.Count; ti++)
        {
            var objectType = objectTypes[ti];
            // Only use cursor from checkpoint for the starting object type.
            var cursorForThisType = ti == startTypeIndex ? afterCursor : null;

            try
            {
                var batch = await FetchObjectsAsync(
                    client, config, objectType, tenantId,
                    after: cursorForThisType, lastModified: lastModified,
                    limit: config.BatchSize,
                    cancellationToken);

                records.AddRange(batch.Records);
                failedRecords += batch.FailedCount;
                errors.AddRange(batch.Errors);

                // If HubSpot returned a next page cursor, yield with checkpoint.
                if (batch.HasMore && batch.NextAfter is not null)
                {
                    var cp = new HubSpotCheckpoint(ti, batch.NextAfter, lastModified);
                    return new FetchResult
                    {
                        Records = records,
                        FailedRecords = failedRecords,
                        Errors = errors,
                        NewCheckpoint = cp.Serialize(),
                        HasMore = true,
                    };
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to fetch {ObjectType} from HubSpot", objectType);
                errors.Add($"Fetch failed for object type '{objectType}': {ex.Message}");
                failedRecords++;
            }

            // Check if we've hit batch size — checkpoint and yield.
            if (records.Count >= config.BatchSize && ti + 1 < objectTypes.Count)
            {
                var cp = new HubSpotCheckpoint(ti + 1, null, lastModified);
                return new FetchResult
                {
                    Records = records,
                    FailedRecords = failedRecords,
                    Errors = errors,
                    NewCheckpoint = cp.Serialize(),
                    HasMore = true,
                };
            }
        }

        // All object types processed — final batch.
        var maxUpdated = records.Count > 0 ? records.Max(r => r.UpdatedAt) : (DateTimeOffset?)null;
        var finalCheckpoint = new HubSpotCheckpoint(0, null,
            maxUpdated ?? parsedCheckpoint?.LastModified ?? DateTimeOffset.UtcNow);

        return new FetchResult
        {
            Records = records,
            FailedRecords = failedRecords,
            Errors = errors,
            NewCheckpoint = finalCheckpoint.Serialize(),
            HasMore = false,
        };
    }

    // --- Object fetch ---

    internal async Task<ObjectFetchBatch> FetchObjectsAsync(
        HttpClient client, HubSpotSourceConfig config, string objectType,
        string tenantId, string? after, DateTimeOffset? lastModified,
        int limit, CancellationToken ct)
    {
        var properties = ResolveProperties(config, objectType);
        var propertiesParam = string.Join(",", properties);
        var effectiveLimit = Math.Min(limit, 100); // HubSpot max page size is 100.

        var url = $"crm/v3/objects/{objectType}?limit={effectiveLimit}&properties={propertiesParam}";

        if (!string.IsNullOrEmpty(after))
            url += $"&after={after}";

        // HubSpot search API supports filtering by lastmodifieddate for incremental sync.
        // For simplicity, use the list endpoint with sort and filter via search API when incremental.
        if (lastModified.HasValue)
        {
            return await FetchObjectsViaSearchAsync(
                client, config, objectType, tenantId, properties,
                after, lastModified.Value, effectiveLimit, ct);
        }

        var response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var result = await DeserializeAsync<HubSpotListResponse>(response, ct);
        if (result?.Results is null)
            return ObjectFetchBatch.Empty;

        var records = new List<CanonicalRecord>();
        var errors = new List<string>();
        var failedCount = 0;

        foreach (var obj in result.Results)
        {
            try
            {
                var record = MapObjectToCanonical(obj, objectType, config.PortalId, tenantId);
                if (record is not null)
                    records.Add(record);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add($"Failed to map {objectType} id={obj.Id}: {ex.Message}");
                failedCount++;
            }
        }

        var nextAfter = result.Paging?.Next?.After;
        return new ObjectFetchBatch(records, failedCount, errors, nextAfter is not null, nextAfter);
    }

    internal async Task<ObjectFetchBatch> FetchObjectsViaSearchAsync(
        HttpClient client, HubSpotSourceConfig config, string objectType,
        string tenantId, IReadOnlyList<string> properties,
        string? after, DateTimeOffset lastModified, int limit, CancellationToken ct)
    {
        var lastModifiedField = objectType switch
        {
            "contacts" => "lastmodifieddate",
            _ => "hs_lastmodifieddate",
        };

        var searchRequest = new HubSpotSearchRequest
        {
            FilterGroups =
            [
                new HubSpotFilterGroup
                {
                    Filters =
                    [
                        new HubSpotFilter
                        {
                            PropertyName = lastModifiedField,
                            Operator = "GTE",
                            Value = lastModified.ToUnixTimeMilliseconds().ToString(),
                        },
                    ],
                },
            ],
            Sorts =
            [
                new HubSpotSort { PropertyName = lastModifiedField, Direction = "ASCENDING" },
            ],
            Properties = properties.ToList(),
            Limit = limit,
            After = after is not null && int.TryParse(after, out var a) ? a : 0,
        };

        var json = JsonSerializer.Serialize(searchRequest, JsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var url = $"crm/v3/objects/{objectType}/search";
        var response = await client.PostAsync(url, content, ct);
        response.EnsureSuccessStatusCode();

        var result = await DeserializeAsync<HubSpotListResponse>(response, ct);
        if (result?.Results is null)
            return ObjectFetchBatch.Empty;

        var records = new List<CanonicalRecord>();
        var errors = new List<string>();
        var failedCount = 0;

        foreach (var obj in result.Results)
        {
            try
            {
                var record = MapObjectToCanonical(obj, objectType, config.PortalId, tenantId);
                if (record is not null)
                    records.Add(record);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add($"Failed to map {objectType} id={obj.Id}: {ex.Message}");
                failedCount++;
            }
        }

        var nextAfter = result.Paging?.Next?.After;
        return new ObjectFetchBatch(records, failedCount, errors, nextAfter is not null, nextAfter);
    }

    // --- Mapping ---

    internal static CanonicalRecord? MapObjectToCanonical(
        HubSpotObject obj, string objectType, string portalId, string tenantId)
    {
        if (obj.Properties is null) return null;

        var id = obj.Id;
        var title = GetTitle(obj.Properties, objectType);
        var textContent = GetTextContent(obj.Properties, objectType);
        var createdAt = ParseHubSpotDate(obj.Properties.GetValueOrDefault("createdate")
            ?? obj.CreatedAt?.ToString("O"));
        var updatedAt = ParseHubSpotDate(obj.Properties.GetValueOrDefault(
            objectType == "contacts" ? "lastmodifieddate" : "hs_lastmodifieddate")
            ?? obj.UpdatedAt?.ToString("O"));

        var deepLink = $"https://app.hubspot.com/contacts/{portalId}/{MapObjectTypeToUrlPath(objectType)}/{id}";
        var contentHash = ComputeHash($"{title}|{textContent}|{updatedAt:O}");

        var pipeline = obj.Properties.GetValueOrDefault("hs_pipeline")
            ?? obj.Properties.GetValueOrDefault("pipeline");
        var priority = obj.Properties.GetValueOrDefault("hs_ticket_priority");
        var stage = obj.Properties.GetValueOrDefault("hs_pipeline_stage")
            ?? obj.Properties.GetValueOrDefault("dealstage")
            ?? obj.Properties.GetValueOrDefault("lifecyclestage");

        var tags = new List<string>();
        if (!string.IsNullOrEmpty(objectType)) tags.Add(objectType);
        if (!string.IsNullOrEmpty(stage)) tags.Add(stage);

        var category = obj.Properties.GetValueOrDefault("hs_ticket_category");

        return new CanonicalRecord
        {
            TenantId = tenantId,
            EvidenceId = $"hubspot-{objectType.TrimEnd('s')}-{id}",
            SourceSystem = ConnectorType.HubSpot,
            SourceType = MapObjectTypeToSourceType(objectType),
            SourceLocator = new SourceLocator(id, deepLink, pipeline),
            Title = title,
            TextContent = textContent,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            Status = MapStageToStatus(stage),
            Permissions = new RecordPermissions(AccessVisibility.Internal, []),
            ContentHash = contentHash,
            AccessLabel = "Internal",
            Author = obj.Properties.GetValueOrDefault("hubspot_owner_id"),
            ProductArea = category,
            Severity = MapPriority(priority),
            Tags = tags,
        };
    }

    // --- Helpers ---

    internal HttpClient CreateHttpClient(string baseUrl, string token)
    {
        var client = _httpClientFactory.CreateClient("HubSpot");
        client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    internal static HubSpotSourceConfig? ParseSourceConfig(string? json, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<HubSpotSourceConfig>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            logger?.LogWarning(ex, "Failed to deserialize HubSpotSourceConfig from JSON");
            return null;
        }
    }

    internal static IReadOnlyList<string> ResolveObjectTypes(HubSpotSourceConfig config)
    {
        if (config.ObjectTypes.Count == 0)
            return ["tickets"];

        return config.ObjectTypes
            .Where(t => SupportedObjectTypes.Contains(t))
            .ToList();
    }

    internal static IReadOnlyList<string> ResolveProperties(HubSpotSourceConfig config, string objectType)
    {
        if (config.CustomProperties.Count > 0)
            return config.CustomProperties;

        return DefaultProperties.TryGetValue(objectType, out var defaults) ? defaults : [];
    }

    private static string GetTitle(Dictionary<string, string?> props, string objectType) => objectType switch
    {
        "tickets" => props.GetValueOrDefault("subject") ?? "(No Subject)",
        "contacts" => $"{props.GetValueOrDefault("firstname") ?? ""} {props.GetValueOrDefault("lastname") ?? ""}".Trim(),
        "companies" => props.GetValueOrDefault("name") ?? "(Unnamed Company)",
        "deals" => props.GetValueOrDefault("dealname") ?? "(Unnamed Deal)",
        _ => props.GetValueOrDefault("subject") ?? props.GetValueOrDefault("name") ?? "(Untitled)",
    };

    private static string GetTextContent(Dictionary<string, string?> props, string objectType) => objectType switch
    {
        "tickets" => props.GetValueOrDefault("content") ?? "",
        "contacts" => BuildContactContent(props),
        "companies" => props.GetValueOrDefault("description") ?? "",
        "deals" => props.GetValueOrDefault("description") ?? "",
        _ => "",
    };

    private static string BuildContactContent(Dictionary<string, string?> props)
    {
        var parts = new List<string>();
        var email = props.GetValueOrDefault("email");
        if (!string.IsNullOrEmpty(email)) parts.Add($"Email: {email}");
        var company = props.GetValueOrDefault("company");
        if (!string.IsNullOrEmpty(company)) parts.Add($"Company: {company}");
        var title = props.GetValueOrDefault("jobtitle");
        if (!string.IsNullOrEmpty(title)) parts.Add($"Title: {title}");
        var stage = props.GetValueOrDefault("lifecyclestage");
        if (!string.IsNullOrEmpty(stage)) parts.Add($"Stage: {stage}");
        return string.Join("; ", parts);
    }

    private static SourceType MapObjectTypeToSourceType(string objectType) => objectType switch
    {
        "tickets" => SourceType.Ticket,
        "contacts" => SourceType.Document,
        "companies" => SourceType.Document,
        "deals" => SourceType.Document,
        _ => SourceType.Document,
    };

    private static string MapObjectTypeToUrlPath(string objectType) => objectType switch
    {
        "tickets" => "ticket",
        "contacts" => "contact",
        "companies" => "company",
        "deals" => "deal",
        _ => objectType.TrimEnd('s'),
    };

    private static EvidenceStatus MapStageToStatus(string? stage)
    {
        if (string.IsNullOrEmpty(stage)) return EvidenceStatus.Open;
        var lower = stage.ToLowerInvariant();
        return lower switch
        {
            "closed" or "closedwon" or "closedlost" or "4" => EvidenceStatus.Closed,
            _ => EvidenceStatus.Open,
        };
    }

    private static string? MapPriority(string? priority)
    {
        if (string.IsNullOrEmpty(priority)) return null;
        return priority.ToUpperInvariant() switch
        {
            "HIGH" => "P1",
            "MEDIUM" => "P2",
            "LOW" => "P3",
            _ => priority,
        };
    }

    internal static DateTimeOffset ParseHubSpotDate(string? value)
    {
        if (string.IsNullOrEmpty(value)) return DateTimeOffset.UtcNow;

        // HubSpot sends either ISO 8601 strings or Unix milliseconds.
        if (DateTimeOffset.TryParse(value, out var dto))
            return dto;

        if (long.TryParse(value, out var ms))
            return DateTimeOffset.FromUnixTimeMilliseconds(ms);

        return DateTimeOffset.UtcNow;
    }

    internal static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static async Task<T?> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, ct);
    }

    private static FetchResult ErrorResult(string error) => new()
    {
        Records = [],
        FailedRecords = 0,
        Errors = [error],
        HasMore = false,
    };

    // --- HubSpot API response models ---

    internal sealed class HubSpotListResponse
    {
        [JsonPropertyName("results")]
        public List<HubSpotObject>? Results { get; set; }

        [JsonPropertyName("paging")]
        public HubSpotPaging? Paging { get; set; }
    }

    internal sealed class HubSpotPaging
    {
        [JsonPropertyName("next")]
        public HubSpotPagingNext? Next { get; set; }
    }

    internal sealed class HubSpotPagingNext
    {
        [JsonPropertyName("after")]
        public string? After { get; set; }

        [JsonPropertyName("link")]
        public string? Link { get; set; }
    }

    internal sealed class HubSpotSearchRequest
    {
        [JsonPropertyName("filterGroups")]
        public List<HubSpotFilterGroup> FilterGroups { get; set; } = [];

        [JsonPropertyName("sorts")]
        public List<HubSpotSort> Sorts { get; set; } = [];

        [JsonPropertyName("properties")]
        public List<string> Properties { get; set; } = [];

        [JsonPropertyName("limit")]
        public int Limit { get; set; } = 100;

        [JsonPropertyName("after")]
        public int After { get; set; }
    }

    internal sealed class HubSpotFilterGroup
    {
        [JsonPropertyName("filters")]
        public List<HubSpotFilter> Filters { get; set; } = [];
    }

    internal sealed class HubSpotFilter
    {
        [JsonPropertyName("propertyName")]
        public required string PropertyName { get; set; }

        [JsonPropertyName("operator")]
        public required string Operator { get; set; }

        [JsonPropertyName("value")]
        public required string Value { get; set; }
    }

    internal sealed class HubSpotSort
    {
        [JsonPropertyName("propertyName")]
        public required string PropertyName { get; set; }

        [JsonPropertyName("direction")]
        public string Direction { get; set; } = "ASCENDING";
    }

    internal sealed record ObjectFetchBatch(
        List<CanonicalRecord> Records,
        int FailedCount,
        List<string> Errors,
        bool HasMore,
        string? NextAfter)
    {
        public static readonly ObjectFetchBatch Empty = new([], 0, [], false, null);
    }
}

/// <summary>
/// HubSpot CRM object (generic across tickets, contacts, companies, deals).
/// </summary>
public sealed class HubSpotObject
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("properties")]
    public Dictionary<string, string?>? Properties { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset? UpdatedAt { get; set; }

    [JsonPropertyName("archived")]
    public bool Archived { get; set; }
}

// --- Checkpoint model ---

/// <summary>
/// Tracks position across multi-object-type HubSpot fetch.
/// Serializes to a compact string for storage in SyncRunEntity.Checkpoint.
/// Format: "{objectTypeIndex}|{afterCursor}|{lastModifiedIso}"
/// </summary>
internal sealed record HubSpotCheckpoint(int ObjectTypeIndex, string? AfterCursor, DateTimeOffset? LastModified)
{
    public string Serialize()
    {
        var cursor = AfterCursor ?? "";
        var modified = LastModified?.ToString("O") ?? "";
        return $"{ObjectTypeIndex}|{cursor}|{modified}";
    }

    public static HubSpotCheckpoint? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var parts = value.Split('|', 3);
        if (parts.Length < 2) return null;

        if (!int.TryParse(parts[0], out var objectTypeIndex)) return null;

        var afterCursor = string.IsNullOrEmpty(parts[1]) ? null : parts[1];

        DateTimeOffset? lastModified = null;
        if (parts.Length == 3 && !string.IsNullOrEmpty(parts[2]) &&
            DateTimeOffset.TryParse(parts[2], out var dt))
        {
            lastModified = dt;
        }

        return new HubSpotCheckpoint(objectTypeIndex, afterCursor, lastModified);
    }
}
