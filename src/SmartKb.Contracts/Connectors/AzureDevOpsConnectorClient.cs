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
/// Azure DevOps connector client. Ingests work items and wiki pages via the ADO REST API.
/// Supports PAT and OAuth authentication. Implements checkpoint-based incremental sync.
/// </summary>
public sealed class AzureDevOpsConnectorClient : IConnectorClient, IEscalationTargetConnector
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private const string ApiVersion = "7.1";
    private const int MaxWiqlResults = 200;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AzureDevOpsConnectorClient> _logger;

    public AzureDevOpsConnectorClient(
        IHttpClientFactory httpClientFactory,
        ILogger<AzureDevOpsConnectorClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public ConnectorType Type => ConnectorType.AzureDevOps;

    public async Task<TestConnectionResponse> TestConnectionAsync(
        string tenantId, string? sourceConfig, string? secretValue,
        CancellationToken cancellationToken = default)
    {
        var config = ParseSourceConfig(sourceConfig);
        if (config is null)
            return new TestConnectionResponse { Success = false, Message = "Invalid or missing source configuration." };

        if (string.IsNullOrEmpty(secretValue))
            return new TestConnectionResponse { Success = false, Message = "No credentials provided. A Personal Access Token is required." };

        try
        {
            using var client = CreateHttpClient(config.OrganizationUrl, secretValue);
            var url = $"_apis/projects?api-version={ApiVersion}&$top=1";
            var response = await client.GetAsync(url, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return new TestConnectionResponse
                {
                    Success = true,
                    Message = "Successfully connected to Azure DevOps.",
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
                Message = "Failed to connect to Azure DevOps.",
                DiagnosticDetail = ex.Message,
            };
        }
    }

    public async Task<IReadOnlyList<CanonicalRecord>> PreviewAsync(
        string tenantId, string? sourceConfig, FieldMappingConfig? fieldMapping,
        string? secretValue, int sampleSize,
        CancellationToken cancellationToken = default)
    {
        var config = ParseSourceConfig(sourceConfig);
        if (config is null || string.IsNullOrEmpty(secretValue))
            return [];

        try
        {
            using var client = CreateHttpClient(config.OrganizationUrl, secretValue);
            var projects = await ResolveProjectsAsync(client, config, cancellationToken);
            if (projects.Count == 0) return [];

            var records = new List<CanonicalRecord>();

            foreach (var project in projects)
            {
                if (records.Count >= sampleSize) break;

                if (config.IngestWorkItems)
                {
                    var workItems = await FetchWorkItemsAsync(
                        client, config, project, tenantId, checkpoint: null,
                        top: sampleSize - records.Count, cancellationToken);
                    records.AddRange(workItems);
                }

                if (records.Count < sampleSize && config.IngestWikiPages)
                {
                    var pages = await FetchWikiPagesAsync(
                        client, config, project, tenantId,
                        top: sampleSize - records.Count, cancellationToken);
                    records.AddRange(pages);
                }
            }

            return records.Take(sampleSize).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Preview failed for ADO connector (tenant={TenantId})", tenantId);
            return [];
        }
    }

    public async Task<FetchResult> FetchAsync(
        string tenantId, string? sourceConfig, FieldMappingConfig? fieldMapping,
        string? secretValue, string? checkpoint, bool isBackfill,
        CancellationToken cancellationToken = default)
    {
        var config = ParseSourceConfig(sourceConfig);
        if (config is null)
            return ErrorResult("Invalid or missing source configuration.");

        if (string.IsNullOrEmpty(secretValue))
            return ErrorResult("No credentials provided.");

        using var client = CreateHttpClient(config.OrganizationUrl, secretValue);

        var parsedCheckpoint = AdoCheckpoint.Parse(checkpoint);
        var projects = await ResolveProjectsAsync(client, config, cancellationToken);
        if (projects.Count == 0)
            return ErrorResult("No accessible projects found.");

        var records = new List<CanonicalRecord>();
        var errors = new List<string>();
        var failedRecords = 0;

        // Determine which project to start from (checkpoint tracks position).
        var startProjectIndex = parsedCheckpoint?.ProjectIndex ?? 0;
        var phase = parsedCheckpoint?.Phase ?? AdoFetchPhase.WorkItems;

        for (var pi = startProjectIndex; pi < projects.Count; pi++)
        {
            var project = projects[pi];

            // Work items phase.
            if (phase == AdoFetchPhase.WorkItems && config.IngestWorkItems)
            {
                try
                {
                    var sinceDate = isBackfill ? null : parsedCheckpoint?.LastModified;
                    var workItems = await FetchWorkItemsAsync(
                        client, config, project, tenantId, sinceDate,
                        config.BatchSize, cancellationToken);
                    records.AddRange(workItems);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Failed to fetch work items from {Project}", project);
                    errors.Add($"Work items fetch failed for project '{project}': {ex.Message}");
                    failedRecords++;
                }
            }

            // Check if we've hit batch size — checkpoint and yield.
            if (records.Count >= config.BatchSize)
            {
                var newCp = new AdoCheckpoint(pi, AdoFetchPhase.WikiPages,
                    records.Max(r => r.UpdatedAt));
                return new FetchResult
                {
                    Records = records,
                    FailedRecords = failedRecords,
                    Errors = errors,
                    NewCheckpoint = newCp.Serialize(),
                    HasMore = true,
                };
            }

            // Wiki pages phase.
            if (config.IngestWikiPages)
            {
                try
                {
                    var pages = await FetchWikiPagesAsync(
                        client, config, project, tenantId,
                        config.BatchSize - records.Count, cancellationToken);
                    records.AddRange(pages);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Failed to fetch wiki pages from {Project}", project);
                    errors.Add($"Wiki pages fetch failed for project '{project}': {ex.Message}");
                    failedRecords++;
                }
            }

            // Reset phase for next project.
            phase = AdoFetchPhase.WorkItems;

            // Yield if we've exceeded batch size.
            if (records.Count >= config.BatchSize)
            {
                var hasMoreProjects = pi + 1 < projects.Count;
                var newCp = new AdoCheckpoint(pi + 1, AdoFetchPhase.WorkItems,
                    records.Count > 0 ? records.Max(r => r.UpdatedAt) : parsedCheckpoint?.LastModified);
                return new FetchResult
                {
                    Records = records,
                    FailedRecords = failedRecords,
                    Errors = errors,
                    NewCheckpoint = newCp.Serialize(),
                    HasMore = hasMoreProjects,
                };
            }
        }

        // All projects processed — final batch.
        var finalCheckpoint = new AdoCheckpoint(0, AdoFetchPhase.WorkItems,
            records.Count > 0 ? records.Max(r => r.UpdatedAt) : parsedCheckpoint?.LastModified ?? DateTimeOffset.UtcNow);

        return new FetchResult
        {
            Records = records,
            FailedRecords = failedRecords,
            Errors = errors,
            NewCheckpoint = finalCheckpoint.Serialize(),
            HasMore = false,
        };
    }

    // --- External Work Item Creation (P1-003) ---

    public async Task<ExternalWorkItemResult> CreateExternalWorkItemAsync(
        string sourceConfig, string secretValue,
        ExternalWorkItemRequest request, CancellationToken ct = default)
    {
        var config = ParseSourceConfig(sourceConfig);
        if (config is null)
            return new ExternalWorkItemResult { Success = false, ErrorDetail = "Invalid source configuration." };

        if (string.IsNullOrEmpty(secretValue))
            return new ExternalWorkItemResult { Success = false, ErrorDetail = "No credentials provided." };

        var project = request.TargetProject;
        if (string.IsNullOrEmpty(project))
        {
            // Fall back to first configured project.
            project = config.Projects.FirstOrDefault();
            if (string.IsNullOrEmpty(project))
                return new ExternalWorkItemResult { Success = false, ErrorDetail = "No target project specified and no projects configured." };
        }

        var workItemType = request.WorkItemType ?? "Bug";

        try
        {
            using var client = CreateHttpClient(config.OrganizationUrl, secretValue);

            var patchOps = new List<object>
            {
                new { op = "add", path = "/fields/System.Title", value = request.Title },
                new { op = "add", path = "/fields/System.Description", value = request.Description },
            };

            if (!string.IsNullOrEmpty(request.AreaPath))
                patchOps.Add(new { op = "add", path = "/fields/System.AreaPath", value = request.AreaPath });

            // Map severity to ADO priority: P1→1, P2→2, P3→3, P4→4.
            var priority = request.Severity switch
            {
                "P1" => 1,
                "P2" => 2,
                "P3" => 3,
                "P4" => 4,
                _ => 3,
            };
            patchOps.Add(new { op = "add", path = "/fields/Microsoft.VSTS.Common.Priority", value = (object)priority });

            var payload = JsonSerializer.Serialize(patchOps, JsonOptions);
            var content = new StringContent(payload, Encoding.UTF8, "application/json-patch+json");

            var url = $"{Uri.EscapeDataString(project)}/_apis/wit/workitems/${Uri.EscapeDataString(workItemType)}?api-version={ApiVersion}";
            var response = await client.PatchAsync(url, content, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("ADO work item creation failed. Status={Status}, Body={Body}",
                    (int)response.StatusCode, errorBody.Length > 500 ? errorBody[..500] : errorBody);
                return new ExternalWorkItemResult
                {
                    Success = false,
                    ErrorDetail = $"ADO API returned {(int)response.StatusCode}: {(errorBody.Length > 200 ? errorBody[..200] : errorBody)}",
                };
            }

            var result = await DeserializeAsync<WorkItemResponse>(response, ct);
            if (result is null)
                return new ExternalWorkItemResult { Success = false, ErrorDetail = "Failed to parse ADO response." };

            var externalId = result.Id.ToString();
            var externalUrl = $"{config.OrganizationUrl.TrimEnd('/')}/{Uri.EscapeDataString(project)}/_workitems/edit/{externalId}";

            _logger.LogInformation("ADO work item created. Id={WorkItemId}, Project={Project}, Type={Type}",
                externalId, project, workItemType);

            return new ExternalWorkItemResult
            {
                Success = true,
                ExternalId = externalId,
                ExternalUrl = externalUrl,
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Failed to create ADO work item");
            return new ExternalWorkItemResult
            {
                Success = false,
                ErrorDetail = $"Connection error: {ex.Message}",
            };
        }
    }

    // --- Work Items ---

    internal async Task<List<CanonicalRecord>> FetchWorkItemsAsync(
        HttpClient client, AzureDevOpsSourceConfig config, string project, string tenantId,
        DateTimeOffset? checkpoint, int top, CancellationToken ct)
    {
        // Build WIQL query.
        var conditions = new List<string> { "[System.TeamProject] = @project" };

        if (config.WorkItemTypes.Count > 0)
        {
            var types = string.Join("', '", config.WorkItemTypes);
            conditions.Add($"[System.WorkItemType] IN ('{types}')");
        }

        if (config.AreaPaths.Count > 0)
        {
            var paths = string.Join("', '", config.AreaPaths);
            conditions.Add($"[System.AreaPath] IN ('{paths}')");
        }

        if (checkpoint.HasValue)
        {
            conditions.Add($"[System.ChangedDate] >= '{checkpoint.Value:yyyy-MM-ddTHH:mm:ssZ}'");
        }

        var wiql = $"SELECT [System.Id] FROM WorkItems WHERE {string.Join(" AND ", conditions)} ORDER BY [System.ChangedDate] ASC";

        var wiqlPayload = JsonSerializer.Serialize(new { query = wiql }, JsonOptions);
        var wiqlContent = new StringContent(wiqlPayload, Encoding.UTF8, "application/json");

        var wiqlUrl = $"{project}/_apis/wit/wiql?api-version={ApiVersion}&$top={Math.Min(top, MaxWiqlResults)}";
        var wiqlResponse = await client.PostAsync(wiqlUrl, wiqlContent, ct);
        wiqlResponse.EnsureSuccessStatusCode();

        var wiqlResult = await DeserializeAsync<WiqlResponse>(wiqlResponse, ct);
        if (wiqlResult?.WorkItems is null || wiqlResult.WorkItems.Count == 0)
            return [];

        // Fetch work item details in batches of 200.
        var ids = wiqlResult.WorkItems.Select(w => w.Id).ToList();
        var records = new List<CanonicalRecord>();

        foreach (var batch in Chunk(ids, MaxWiqlResults))
        {
            var idList = string.Join(",", batch);
            var fields = "System.Id,System.Title,System.Description,System.WorkItemType,System.State,System.AreaPath,System.AssignedTo,System.CreatedDate,System.ChangedDate,System.Tags";
            var detailUrl = $"_apis/wit/workitems?ids={idList}&fields={fields}&api-version={ApiVersion}";
            var detailResponse = await client.GetAsync(detailUrl, ct);
            detailResponse.EnsureSuccessStatusCode();

            var detailResult = await DeserializeAsync<WorkItemListResponse>(detailResponse, ct);
            if (detailResult?.Value is null) continue;

            foreach (var wi in detailResult.Value)
            {
                var record = MapWorkItemToCanonical(wi, config.OrganizationUrl, project, tenantId);
                if (record is not null)
                    records.Add(record);
            }
        }

        return records;
    }

    private static CanonicalRecord? MapWorkItemToCanonical(
        WorkItemResponse wi, string orgUrl, string project, string tenantId)
    {
        var fields = wi.Fields;
        if (fields is null) return null;

        var id = wi.Id.ToString();
        var title = fields.GetValueOrDefault("System.Title")?.ToString() ?? "";
        var description = fields.GetValueOrDefault("System.Description")?.ToString() ?? "";
        var workItemType = fields.GetValueOrDefault("System.WorkItemType")?.ToString() ?? "WorkItem";
        var state = fields.GetValueOrDefault("System.State")?.ToString() ?? "";
        var areaPath = fields.GetValueOrDefault("System.AreaPath")?.ToString() ?? "";
        var assignedTo = fields.GetValueOrDefault("System.AssignedTo");
        var tags = fields.GetValueOrDefault("System.Tags")?.ToString() ?? "";

        var createdDate = ParseDateField(fields.GetValueOrDefault("System.CreatedDate"));
        var changedDate = ParseDateField(fields.GetValueOrDefault("System.ChangedDate"));

        var author = assignedTo is JsonElement { ValueKind: JsonValueKind.Object } assignedObj
            ? assignedObj.TryGetProperty("displayName", out var displayName) ? displayName.GetString() : null
            : assignedTo?.ToString();

        var deepLink = $"{orgUrl.TrimEnd('/')}/{Uri.EscapeDataString(project)}/_workitems/edit/{id}";
        var contentForHash = $"{title}|{description}|{state}|{changedDate:O}";
        var contentHash = ComputeHash(contentForHash);

        // Map area path to ACL groups.
        var allowedGroups = string.IsNullOrEmpty(areaPath) ? [] : new List<string> { areaPath };

        var tagList = string.IsNullOrWhiteSpace(tags)
            ? (IReadOnlyList<string>)[]
            : tags.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return new CanonicalRecord
        {
            TenantId = tenantId,
            EvidenceId = $"ado-wi-{id}",
            SourceSystem = ConnectorType.AzureDevOps,
            SourceType = MapWorkItemType(workItemType),
            SourceLocator = new SourceLocator(id, deepLink),
            Title = title,
            TextContent = StripHtml(description),
            CreatedAt = createdDate,
            UpdatedAt = changedDate,
            Status = MapState(state),
            Permissions = new RecordPermissions(
                string.IsNullOrEmpty(areaPath) ? AccessVisibility.Internal : AccessVisibility.Restricted,
                allowedGroups),
            ContentHash = contentHash,
            AccessLabel = string.IsNullOrEmpty(areaPath) ? "Internal" : $"Restricted ({areaPath})",
            Author = author,
            ProductArea = areaPath,
            Tags = tagList,
        };
    }

    // --- Wiki Pages ---

    internal async Task<List<CanonicalRecord>> FetchWikiPagesAsync(
        HttpClient client, AzureDevOpsSourceConfig config, string project, string tenantId,
        int top, CancellationToken ct)
    {
        // List wikis in project.
        var wikisUrl = $"{project}/_apis/wiki/wikis?api-version={ApiVersion}";
        var wikisResponse = await client.GetAsync(wikisUrl, ct);

        if (!wikisResponse.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to list wikis for project {Project}: {Status}",
                project, wikisResponse.StatusCode);
            return [];
        }

        var wikisResult = await DeserializeAsync<WikiListResponse>(wikisResponse, ct);
        if (wikisResult?.Value is null || wikisResult.Value.Count == 0)
            return [];

        var records = new List<CanonicalRecord>();

        foreach (var wiki in wikisResult.Value)
        {
            if (records.Count >= top) break;

            try
            {
                var pages = await FetchWikiPagesRecursiveAsync(
                    client, config, project, wiki, tenantId,
                    top - records.Count, ct);
                records.AddRange(pages);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to fetch pages from wiki {WikiName} in {Project}",
                    wiki.Name, project);
            }
        }

        return records;
    }

    private async Task<List<CanonicalRecord>> FetchWikiPagesRecursiveAsync(
        HttpClient client, AzureDevOpsSourceConfig config, string project,
        WikiResponse wiki, string tenantId, int remaining, CancellationToken ct)
    {
        var pagesUrl = $"{project}/_apis/wiki/wikis/{wiki.Id}/pages?api-version={ApiVersion}&recursionLevel=full&includeContent=false";
        var pagesResponse = await client.GetAsync(pagesUrl, ct);
        pagesResponse.EnsureSuccessStatusCode();

        var rootPage = await DeserializeAsync<WikiPageResponse>(pagesResponse, ct);
        if (rootPage is null) return [];

        var allPages = FlattenWikiPages(rootPage);
        var records = new List<CanonicalRecord>();

        foreach (var page in allPages.Take(remaining))
        {
            // Fetch individual page content.
            try
            {
                var pageContentUrl = $"{project}/_apis/wiki/wikis/{wiki.Id}/pages?path={Uri.EscapeDataString(page.Path)}&includeContent=true&api-version={ApiVersion}";
                var contentResponse = await client.GetAsync(pageContentUrl, ct);
                if (!contentResponse.IsSuccessStatusCode) continue;

                var pageDetail = await DeserializeAsync<WikiPageResponse>(contentResponse, ct);
                if (pageDetail is null) continue;

                var record = MapWikiPageToCanonical(pageDetail, wiki, config.OrganizationUrl, project, tenantId);
                records.Add(record);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to fetch wiki page content at {Path}", page.Path);
            }
        }

        return records;
    }

    private static CanonicalRecord MapWikiPageToCanonical(
        WikiPageResponse page, WikiResponse wiki, string orgUrl, string project, string tenantId)
    {
        var pageId = page.Id.ToString();
        var title = ExtractPageTitle(page.Path);
        var content = page.Content ?? "";
        var deepLink = $"{orgUrl.TrimEnd('/')}/{Uri.EscapeDataString(project)}/_wiki/wikis/{Uri.EscapeDataString(wiki.Name)}/{pageId}";

        var contentHash = ComputeHash($"{title}|{content}");

        return new CanonicalRecord
        {
            TenantId = tenantId,
            EvidenceId = $"ado-wiki-{wiki.Id}-{pageId}",
            SourceSystem = ConnectorType.AzureDevOps,
            SourceType = SourceType.WikiPage,
            SourceLocator = new SourceLocator(pageId, deepLink),
            Title = title,
            TextContent = content,
            CreatedAt = page.GitItemPath is not null ? DateTimeOffset.UtcNow : DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Status = EvidenceStatus.Open,
            Permissions = new RecordPermissions(AccessVisibility.Internal, []),
            ContentHash = contentHash,
            AccessLabel = "Internal",
        };
    }

    // --- Helpers ---

    internal HttpClient CreateHttpClient(string organizationUrl, string pat)
    {
        var client = _httpClientFactory.CreateClient("AzureDevOps");
        client.BaseAddress = new Uri(organizationUrl.TrimEnd('/') + "/");

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{pat}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        return client;
    }

    internal static AzureDevOpsSourceConfig? ParseSourceConfig(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<AzureDevOpsSourceConfig>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<List<string>> ResolveProjectsAsync(
        HttpClient client, AzureDevOpsSourceConfig config, CancellationToken ct)
    {
        if (config.Projects.Count > 0)
            return config.Projects.ToList();

        // List all accessible projects.
        var url = $"_apis/projects?api-version={ApiVersion}";
        var response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var result = await DeserializeAsync<ProjectListResponse>(response, ct);
        return result?.Value?.Select(p => p.Name).ToList() ?? [];
    }

    private static async Task<T?> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, ct);
    }

    private static List<WikiPageResponse> FlattenWikiPages(WikiPageResponse root)
    {
        var result = new List<WikiPageResponse>();
        FlattenRecursive(root, result);
        return result;
    }

    private static void FlattenRecursive(WikiPageResponse page, List<WikiPageResponse> result)
    {
        // Skip root page (path = "/").
        if (page.Path != "/")
            result.Add(page);

        if (page.SubPages is null) return;
        foreach (var sub in page.SubPages)
            FlattenRecursive(sub, result);
    }

    private static string ExtractPageTitle(string path)
    {
        var segments = path.Split('/');
        var lastSegment = segments.LastOrDefault(s => !string.IsNullOrEmpty(s)) ?? path;
        return lastSegment.Replace('-', ' ');
    }

    private static SourceType MapWorkItemType(string workItemType) => workItemType.ToLowerInvariant() switch
    {
        "bug" => SourceType.WorkItem,
        "task" => SourceType.Task,
        "user story" or "product backlog item" or "feature" or "epic" => SourceType.WorkItem,
        _ => SourceType.WorkItem,
    };

    private static EvidenceStatus MapState(string state) => state.ToLowerInvariant() switch
    {
        "closed" or "done" or "resolved" or "completed" => EvidenceStatus.Closed,
        "removed" => EvidenceStatus.Deleted,
        "new" or "active" or "committed" or "approved" => EvidenceStatus.Open,
        _ => EvidenceStatus.Open,
    };

    private static DateTimeOffset ParseDateField(object? value)
    {
        if (value is JsonElement el)
        {
            if (el.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(el.GetString(), out var dto))
                return dto;
        }
        if (value is string s && DateTimeOffset.TryParse(s, out var dto2))
            return dto2;
        return DateTimeOffset.UtcNow;
    }

    internal static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    internal static string StripHtml(string html)
    {
        if (string.IsNullOrEmpty(html)) return "";

        // Simple HTML tag removal for work item descriptions.
        var result = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\s+", " ");
        return result.Trim();
    }

    private static FetchResult ErrorResult(string error) => new()
    {
        Records = [],
        FailedRecords = 0,
        Errors = [error],
        HasMore = false,
    };

    private static IEnumerable<List<T>> Chunk<T>(List<T> source, int size)
    {
        for (var i = 0; i < source.Count; i += size)
            yield return source.GetRange(i, Math.Min(size, source.Count - i));
    }

    // --- ADO API response models ---

    internal sealed class WiqlResponse
    {
        [JsonPropertyName("workItems")]
        public List<WiqlWorkItemRef>? WorkItems { get; set; }
    }

    internal sealed class WiqlWorkItemRef
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }
    }

    internal sealed class WorkItemListResponse
    {
        [JsonPropertyName("value")]
        public List<WorkItemResponse>? Value { get; set; }
    }

    internal sealed class WorkItemResponse
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("fields")]
        public Dictionary<string, object?>? Fields { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }
    }

    internal sealed class ProjectListResponse
    {
        [JsonPropertyName("value")]
        public List<ProjectResponse>? Value { get; set; }
    }

    internal sealed class ProjectResponse
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";
    }

    internal sealed class WikiListResponse
    {
        [JsonPropertyName("value")]
        public List<WikiResponse>? Value { get; set; }
    }

    internal sealed class WikiResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";
    }

    internal sealed class WikiPageResponse
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("path")]
        public string Path { get; set; } = "/";

        [JsonPropertyName("content")]
        public string? Content { get; set; }

        [JsonPropertyName("gitItemPath")]
        public string? GitItemPath { get; set; }

        [JsonPropertyName("subPages")]
        public List<WikiPageResponse>? SubPages { get; set; }
    }
}

// --- Checkpoint model ---

internal enum AdoFetchPhase
{
    WorkItems,
    WikiPages,
}

/// <summary>
/// Tracks position across multi-project, multi-phase ADO fetch.
/// Serializes to a compact string for storage in SyncRunEntity.Checkpoint.
/// Format: "{projectIndex}|{phase}|{lastModifiedIso}"
/// </summary>
internal sealed record AdoCheckpoint(int ProjectIndex, AdoFetchPhase Phase, DateTimeOffset? LastModified)
{
    public string Serialize()
    {
        var modified = LastModified?.ToString("O") ?? "";
        return $"{ProjectIndex}|{Phase}|{modified}";
    }

    public static AdoCheckpoint? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var parts = value.Split('|', 3);
        if (parts.Length < 2) return null;

        if (!int.TryParse(parts[0], out var projectIndex)) return null;
        if (!Enum.TryParse<AdoFetchPhase>(parts[1], out var phase)) return null;

        DateTimeOffset? lastModified = null;
        if (parts.Length == 3 && !string.IsNullOrEmpty(parts[2]) &&
            DateTimeOffset.TryParse(parts[2], out var dt))
        {
            lastModified = dt;
        }

        return new AdoCheckpoint(projectIndex, phase, lastModified);
    }
}
