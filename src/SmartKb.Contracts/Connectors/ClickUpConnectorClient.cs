using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;

namespace SmartKb.Contracts.Connectors;

/// <summary>
/// ClickUp connector client. Ingests tasks and docs via the ClickUp REST API v2.
/// Supports PAT/OAuth authentication. Implements checkpoint-based incremental sync
/// via date_updated_gt filter.
/// </summary>
public sealed class ClickUpConnectorClient : IConnectorClient, IEscalationTargetConnector
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ClickUpConnectorClient> _logger;

    public ClickUpConnectorClient(
        IHttpClientFactory httpClientFactory,
        ILogger<ClickUpConnectorClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public ConnectorType Type => ConnectorType.ClickUp;

    public async Task<TestConnectionResponse> TestConnectionAsync(
        string tenantId, string? sourceConfig, string? secretValue,
        CancellationToken cancellationToken = default)
    {
        var config = ParseSourceConfig(sourceConfig, _logger);
        if (config is null)
            return new TestConnectionResponse { Success = false, Message = "Invalid or missing source configuration." };

        if (string.IsNullOrEmpty(secretValue))
            return new TestConnectionResponse { Success = false, Message = "No credentials provided. A ClickUp API token is required." };

        try
        {
            using var client = CreateHttpClient(config.BaseUrl, secretValue);
            using var response = await client.GetAsync("api/v2/user", cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return new TestConnectionResponse
                {
                    Success = true,
                    Message = $"Successfully connected to ClickUp workspace {config.WorkspaceId}.",
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
            _logger.LogWarning(ex, "ClickUp test connection failed");
            return new TestConnectionResponse
            {
                Success = false,
                Message = "Failed to connect to ClickUp.",
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

            if (config.IngestTasks)
            {
                var listIds = await ResolveListIdsAsync(client, config, cancellationToken);
                foreach (var listId in listIds)
                {
                    if (records.Count >= sampleSize) break;

                    var batch = await FetchTasksAsync(
                        client, config, listId, tenantId,
                        page: 0, dateUpdatedGt: null,
                        cancellationToken);
                    records.AddRange(batch.Records);
                }
            }

            return records.Take(sampleSize).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Preview failed for ClickUp connector (tenant={TenantId})", tenantId);
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
            return FetchResult.Error("Invalid or missing source configuration.");

        if (string.IsNullOrEmpty(secretValue))
            return FetchResult.Error("No credentials provided.");

        using var client = CreateHttpClient(config.BaseUrl, secretValue);

        var parsedCheckpoint = ClickUpCheckpoint.Parse(checkpoint);
        var records = new List<CanonicalRecord>();
        var errors = new List<string>();
        var failedRecords = 0;

        var dateUpdatedGt = isBackfill ? null : parsedCheckpoint?.LastModified;

        // Resolve list IDs from workspace hierarchy.
        List<string> listIds;
        try
        {
            listIds = await ResolveListIdsAsync(client, config, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to resolve ClickUp list IDs");
            return FetchResult.Error($"Failed to resolve list IDs: {ex.Message}");
        }

        if (listIds.Count == 0)
            return FetchResult.Error("No lists found in the configured workspace/spaces.");

        var startListIndex = parsedCheckpoint?.ListIndex ?? 0;
        var startPage = parsedCheckpoint?.Page ?? 0;

        for (var li = startListIndex; li < listIds.Count; li++)
        {
            var listId = listIds[li];
            var page = li == startListIndex ? startPage : 0;

            try
            {
                var batch = await FetchTasksAsync(
                    client, config, listId, tenantId,
                    page: page, dateUpdatedGt: dateUpdatedGt,
                    cancellationToken);

                records.AddRange(batch.Records);
                failedRecords += batch.FailedCount;
                errors.AddRange(batch.Errors);

                // If there are more pages, yield with checkpoint.
                if (batch.HasMore)
                {
                    var cp = new ClickUpCheckpoint(li, page + 1, dateUpdatedGt);
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
                _logger.LogWarning(ex, "Failed to fetch tasks from ClickUp list {ListId}", listId);
                errors.Add($"Fetch failed for list '{listId}': {ex.Message}");
                failedRecords++;
            }

            // Check batch size threshold — yield with checkpoint.
            if (records.Count >= config.BatchSize && li + 1 < listIds.Count)
            {
                var cp = new ClickUpCheckpoint(li + 1, 0, dateUpdatedGt);
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

        // All lists processed — final batch.
        var maxUpdated = records.Count > 0 ? records.Max(r => r.UpdatedAt) : (DateTimeOffset?)null;
        var finalCheckpoint = new ClickUpCheckpoint(0, 0,
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

    // --- External Task Creation (P1-003) ---

    public async Task<ExternalWorkItemResult> CreateExternalWorkItemAsync(
        string sourceConfig, string secretValue,
        ExternalWorkItemRequest request, CancellationToken ct = default)
    {
        var config = ParseSourceConfig(sourceConfig, _logger);
        if (config is null)
            return new ExternalWorkItemResult { Success = false, ErrorDetail = "Invalid source configuration." };

        if (string.IsNullOrEmpty(secretValue))
            return new ExternalWorkItemResult { Success = false, ErrorDetail = "No credentials provided." };

        var listId = request.TargetListId;
        if (string.IsNullOrEmpty(listId))
        {
            // Fall back to first configured list, or resolve the first list from workspace.
            listId = config.ListIds.FirstOrDefault();
            if (string.IsNullOrEmpty(listId))
            {
                try
                {
                    using var resolveClient = CreateHttpClient(config.BaseUrl, secretValue);
                    var listIds = await ResolveListIdsAsync(resolveClient, config, ct);
                    listId = listIds.FirstOrDefault();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Failed to resolve ClickUp list IDs for escalation");
                    return new ExternalWorkItemResult
                    {
                        Success = false,
                        ErrorDetail = $"No target list specified and failed to resolve lists: {ex.Message}",
                    };
                }

                if (string.IsNullOrEmpty(listId))
                    return new ExternalWorkItemResult { Success = false, ErrorDetail = "No target list specified and no lists found in workspace." };
            }
        }

        try
        {
            using var client = CreateHttpClient(config.BaseUrl, secretValue);

            // Map severity to ClickUp priority: P1→1(Urgent), P2→2(High), P3→3(Normal), P4→4(Low).
            int? priority = request.Severity switch
            {
                "P1" => 1,
                "P2" => 2,
                "P3" => 3,
                "P4" => 4,
                _ => 3,
            };

            var body = new Dictionary<string, object?>
            {
                ["name"] = request.Title,
                ["description"] = request.Description,
                ["priority"] = priority,
            };

            var payload = JsonSerializer.Serialize(body, SharedJsonOptions.CamelCaseIgnoreNull);
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");

            var url = $"api/v2/list/{listId}/task";
            using var response = await client.PostAsync(url, content, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("ClickUp task creation failed. Status={Status}, Body={Body}",
                    (int)response.StatusCode, errorBody.Length > 500 ? errorBody[..500] : errorBody);
                return new ExternalWorkItemResult
                {
                    Success = false,
                    ErrorDetail = $"ClickUp API returned {(int)response.StatusCode}: {(errorBody.Length > 200 ? errorBody[..200] : errorBody)}",
                };
            }

            var result = await DeserializeAsync<ClickUpTask>(response, ct);
            if (result is null || string.IsNullOrEmpty(result.Id))
                return new ExternalWorkItemResult { Success = false, ErrorDetail = "Failed to parse ClickUp response." };

            var externalUrl = result.Url ?? $"https://app.clickup.com/t/{result.Id}";

            _logger.LogInformation("ClickUp task created. Id={TaskId}, ListId={ListId}",
                result.Id, listId);

            return new ExternalWorkItemResult
            {
                Success = true,
                ExternalId = result.Id,
                ExternalUrl = externalUrl,
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Failed to create ClickUp task");
            return new ExternalWorkItemResult
            {
                Success = false,
                ErrorDetail = $"Connection error: {ex.Message}",
            };
        }
    }

    // --- Task fetch ---

    internal async Task<TaskFetchBatch> FetchTasksAsync(
        HttpClient client, ClickUpSourceConfig config, string listId,
        string tenantId, int page, DateTimeOffset? dateUpdatedGt,
        CancellationToken ct)
    {
        var url = $"api/v2/list/{listId}/task?page={page}&subtasks=true&include_closed=true";

        if (dateUpdatedGt.HasValue)
            url += $"&date_updated_gt={dateUpdatedGt.Value.ToUnixTimeMilliseconds()}";

        if (config.TaskStatuses.Count > 0)
        {
            foreach (var status in config.TaskStatuses)
                url += $"&statuses[]={Uri.EscapeDataString(status)}";
        }

        using var response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var result = await DeserializeAsync<ClickUpTaskListResponse>(response, ct);
        if (result?.Tasks is null)
            return TaskFetchBatch.Empty;

        var records = new List<CanonicalRecord>();
        var errors = new List<string>();
        var failedCount = 0;

        foreach (var task in result.Tasks)
        {
            try
            {
                var record = MapTaskToCanonical(task, config.WorkspaceId, tenantId);
                if (record is not null)
                    records.Add(record);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to map ClickUp task id={TaskId}", task.Id);
                errors.Add($"Failed to map task id={task.Id}: {ex.Message}");
                failedCount++;
            }
        }

        // ClickUp tasks API: if the number returned equals the page size, there may be more.
        var hasMore = result.Tasks.Count > 0 && !result.LastPage;
        return new TaskFetchBatch(records, failedCount, errors, hasMore);
    }

    // --- List resolution ---

    internal async Task<List<string>> ResolveListIdsAsync(
        HttpClient client, ClickUpSourceConfig config, CancellationToken ct)
    {
        // If specific list IDs are configured, use them directly.
        if (config.ListIds.Count > 0)
            return config.ListIds.ToList();

        var listIds = new List<string>();

        // If specific folder IDs are configured, get lists from those folders.
        if (config.FolderIds.Count > 0)
        {
            foreach (var folderId in config.FolderIds)
            {
                var folderLists = await GetListsFromFolderAsync(client, folderId, ct);
                listIds.AddRange(folderLists);
            }
            return listIds;
        }

        // Resolve from spaces → folders → lists.
        var spaceIds = config.SpaceIds.Count > 0
            ? config.SpaceIds.ToList()
            : await GetSpaceIdsAsync(client, config.WorkspaceId, ct);

        foreach (var spaceId in spaceIds)
        {
            // Get folderless lists.
            var folderlessLists = await GetFolderlessListsAsync(client, spaceId, ct);
            listIds.AddRange(folderlessLists);

            // Get folders and their lists.
            var folderIds = await GetFolderIdsAsync(client, spaceId, ct);
            foreach (var folderId in folderIds)
            {
                var folderLists = await GetListsFromFolderAsync(client, folderId, ct);
                listIds.AddRange(folderLists);
            }
        }

        return listIds;
    }

    internal async Task<List<string>> GetSpaceIdsAsync(HttpClient client, string workspaceId, CancellationToken ct)
    {
        using var response = await client.GetAsync($"api/v2/team/{workspaceId}/space?archived=false", ct);
        response.EnsureSuccessStatusCode();
        var result = await DeserializeAsync<ClickUpSpacesResponse>(response, ct);
        return result?.Spaces?.Select(s => s.Id).ToList() ?? [];
    }

    internal async Task<List<string>> GetFolderIdsAsync(HttpClient client, string spaceId, CancellationToken ct)
    {
        using var response = await client.GetAsync($"api/v2/space/{spaceId}/folder?archived=false", ct);
        response.EnsureSuccessStatusCode();
        var result = await DeserializeAsync<ClickUpFoldersResponse>(response, ct);
        return result?.Folders?.Select(f => f.Id).ToList() ?? [];
    }

    internal async Task<List<string>> GetListsFromFolderAsync(HttpClient client, string folderId, CancellationToken ct)
    {
        using var response = await client.GetAsync($"api/v2/folder/{folderId}/list?archived=false", ct);
        response.EnsureSuccessStatusCode();
        var result = await DeserializeAsync<ClickUpListsResponse>(response, ct);
        return result?.Lists?.Select(l => l.Id).ToList() ?? [];
    }

    internal async Task<List<string>> GetFolderlessListsAsync(HttpClient client, string spaceId, CancellationToken ct)
    {
        using var response = await client.GetAsync($"api/v2/space/{spaceId}/list?archived=false", ct);
        response.EnsureSuccessStatusCode();
        var result = await DeserializeAsync<ClickUpListsResponse>(response, ct);
        return result?.Lists?.Select(l => l.Id).ToList() ?? [];
    }

    // --- Mapping ---

    internal static CanonicalRecord? MapTaskToCanonical(
        ClickUpTask task, string workspaceId, string tenantId)
    {
        if (string.IsNullOrEmpty(task.Id)) return null;

        var title = task.Name ?? "(Untitled Task)";
        var textContent = task.TextContent ?? task.Description ?? "";
        var createdAt = ParseClickUpTimestamp(task.DateCreated);
        var updatedAt = ParseClickUpTimestamp(task.DateUpdated);

        var deepLink = task.Url ?? $"https://app.clickup.com/t/{task.Id}";
        var contentHash = ConnectorHttpHelper.ComputeHash($"{title}|{textContent}|{updatedAt:O}");

        var tags = new List<string>();
        if (task.Tags is not null)
        {
            foreach (var tag in task.Tags)
            {
                if (!string.IsNullOrEmpty(tag.Name))
                    tags.Add(tag.Name);
            }
        }

        var listName = task.List?.Name;
        if (!string.IsNullOrEmpty(listName))
            tags.Add(listName);

        var spaceName = task.Space?.Id;

        // Map priority: 1=Urgent→P1, 2=High→P2, 3=Normal→P3, 4=Low→P4.
        var severity = MapPriority(task.Priority);

        // Map status.
        var status = MapTaskStatus(task.Status);

        // ACL: ClickUp workspace-level access → Internal visibility.
        // Space name used as access label for grouping.
        var accessLabel = VisibilityLevel.Internal;

        return new CanonicalRecord
        {
            TenantId = tenantId,
            EvidenceId = $"clickup-task-{task.Id}",
            SourceSystem = ConnectorType.ClickUp,
            SourceType = SourceType.Task,
            SourceLocator = new SourceLocator(task.Id, deepLink, listName),
            Title = title,
            TextContent = textContent,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            Status = status,
            Permissions = new RecordPermissions(AccessVisibility.Internal, []),
            ContentHash = contentHash,
            AccessLabel = accessLabel,
            Author = task.Assignees?.FirstOrDefault()?.Username,
            ProductArea = spaceName,
            Severity = severity,
            Tags = tags,
        };
    }

    // --- Helpers ---

    internal HttpClient CreateHttpClient(string baseUrl, string token)
    {
        var client = _httpClientFactory.CreateClient(HttpClientNames.ClickUp);
        ConnectorHttpHelper.ConfigureBearerClient(client, baseUrl, token);
        return client;
    }

    internal static ClickUpSourceConfig? ParseSourceConfig(string? json, ILogger? logger = null)
        => ConnectorHttpHelper.ParseJson<ClickUpSourceConfig>(json, SharedJsonOptions.CamelCaseIgnoreNull, logger);

    internal static DateTimeOffset ParseClickUpTimestamp(string? value)
    {
        if (string.IsNullOrEmpty(value)) return DateTimeOffset.UtcNow;

        // ClickUp sends Unix milliseconds as strings.
        if (long.TryParse(value, out var ms))
            return DateTimeOffset.FromUnixTimeMilliseconds(ms);

        if (DateTimeOffset.TryParse(value, out var dto))
            return dto;

        return DateTimeOffset.UtcNow;
    }

    private static string? MapPriority(ClickUpPriority? priority)
    {
        if (priority is null) return null;
        return priority.Id switch
        {
            "1" => "P1", // Urgent
            "2" => "P2", // High
            "3" => "P3", // Normal
            "4" => "P4", // Low
            _ => null,
        };
    }

    private static EvidenceStatus MapTaskStatus(ClickUpStatus? status)
    {
        if (status is null) return EvidenceStatus.Open;
        var type = status.Type?.ToLowerInvariant();
        return type switch
        {
            "closed" or "done" => EvidenceStatus.Closed,
            _ => EvidenceStatus.Open,
        };
    }

    private Task<T?> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken ct)
        => ConnectorHttpHelper.DeserializeAsync<T>(response, SharedJsonOptions.CamelCaseIgnoreNull, ct, _logger);

    // --- ClickUp API response models ---

    internal sealed class ClickUpTaskListResponse
    {
        [JsonPropertyName("tasks")]
        public List<ClickUpTask>? Tasks { get; set; }

        [JsonPropertyName("last_page")]
        public bool LastPage { get; set; }
    }

    internal sealed class ClickUpSpacesResponse
    {
        [JsonPropertyName("spaces")]
        public List<ClickUpIdName>? Spaces { get; set; }
    }

    internal sealed class ClickUpFoldersResponse
    {
        [JsonPropertyName("folders")]
        public List<ClickUpIdName>? Folders { get; set; }
    }

    internal sealed class ClickUpListsResponse
    {
        [JsonPropertyName("lists")]
        public List<ClickUpIdName>? Lists { get; set; }
    }

    internal sealed record TaskFetchBatch(
        List<CanonicalRecord> Records,
        int FailedCount,
        List<string> Errors,
        bool HasMore)
    {
        public static readonly TaskFetchBatch Empty = new([], 0, [], false);
    }
}

/// <summary>
/// ClickUp task object from the Tasks API.
/// </summary>
public sealed class ClickUpTask
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("text_content")]
    public string? TextContent { get; set; }

    [JsonPropertyName("status")]
    public ClickUpStatus? Status { get; set; }

    [JsonPropertyName("priority")]
    public ClickUpPriority? Priority { get; set; }

    [JsonPropertyName("date_created")]
    public string? DateCreated { get; set; }

    [JsonPropertyName("date_updated")]
    public string? DateUpdated { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("tags")]
    public List<ClickUpTag>? Tags { get; set; }

    [JsonPropertyName("assignees")]
    public List<ClickUpUser>? Assignees { get; set; }

    [JsonPropertyName("list")]
    public ClickUpIdName? List { get; set; }

    [JsonPropertyName("space")]
    public ClickUpIdName? Space { get; set; }

    [JsonPropertyName("folder")]
    public ClickUpIdName? Folder { get; set; }
}

public sealed class ClickUpStatus
{
    [JsonPropertyName("status")]
    public string? StatusName { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("color")]
    public string? Color { get; set; }
}

public sealed class ClickUpPriority
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("priority")]
    public string? PriorityName { get; set; }

    [JsonPropertyName("color")]
    public string? Color { get; set; }
}

public sealed class ClickUpTag
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public sealed class ClickUpUser
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }
}

public sealed class ClickUpIdName
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

// --- Checkpoint model ---

/// <summary>
/// Tracks position across multi-list ClickUp fetch.
/// Format: "{listIndex}|{page}|{lastModifiedIso}"
/// </summary>
internal sealed record ClickUpCheckpoint(int ListIndex, int Page, DateTimeOffset? LastModified)
{
    public string Serialize()
    {
        var modified = LastModified?.ToString("O") ?? "";
        return $"{ListIndex}|{Page}|{modified}";
    }

    public static ClickUpCheckpoint? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var parts = value.Split('|', 3);
        if (parts.Length < 2) return null;

        if (!int.TryParse(parts[0], out var listIndex)) return null;
        if (!int.TryParse(parts[1], out var page)) return null;

        DateTimeOffset? lastModified = null;
        if (parts.Length == 3 && !string.IsNullOrEmpty(parts[2]) &&
            DateTimeOffset.TryParse(parts[2], out var dt))
        {
            lastModified = dt;
        }

        return new ClickUpCheckpoint(listIndex, page, lastModified);
    }
}
