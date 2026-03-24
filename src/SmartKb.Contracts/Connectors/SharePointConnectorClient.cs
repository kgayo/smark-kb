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
/// SharePoint connector client. Ingests document library files via Microsoft Graph REST API.
/// Uses OAuth2 client credentials flow for authentication and delta queries for incremental sync.
/// Handles delta token expiry (410 Gone) by falling back to full sync.
/// </summary>
public sealed class SharePointConnectorClient : IConnectorClient
{
    private const string GraphBaseUrl = "https://graph.microsoft.com/v1.0";
    private const string GraphTokenUrl = "https://login.microsoftonline.com/{0}/oauth2/v2.0/token";

    // File extensions we can extract text from. Others are skipped.
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".csv", ".log", ".json", ".xml", ".html", ".htm",
        ".docx", ".doc", ".pdf", ".pptx", ".ppt", ".xlsx", ".xls",
        ".rtf", ".odt", ".ods", ".odp",
    };

    // Binary file extensions that require text extraction via ITextExtractionService.
    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".docx", ".pptx", ".xlsx",
    };

    // Text-based extensions where we can download content directly as UTF-8 text.
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".csv", ".log", ".json", ".xml", ".html", ".htm",
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ITextExtractionService _textExtractor;
    private readonly ILogger<SharePointConnectorClient> _logger;

    public SharePointConnectorClient(
        IHttpClientFactory httpClientFactory,
        ITextExtractionService textExtractor,
        ILogger<SharePointConnectorClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _textExtractor = textExtractor;
        _logger = logger;
    }

    public ConnectorType Type => ConnectorType.SharePoint;

    public async Task<TestConnectionResponse> TestConnectionAsync(
        string tenantId, string? sourceConfig, string? secretValue,
        CancellationToken cancellationToken = default)
    {
        var config = ParseSourceConfig(sourceConfig, _logger);
        if (config is null)
            return new TestConnectionResponse { Success = false, Message = "Invalid or missing source configuration." };

        if (string.IsNullOrEmpty(secretValue))
            return new TestConnectionResponse { Success = false, Message = "No credentials provided. A client secret is required." };

        try
        {
            var accessToken = await AcquireTokenAsync(config.EntraIdTenantId, config.ClientId, secretValue, cancellationToken);
            using var client = CreateGraphClient(accessToken);

            var siteId = await ResolveSiteIdAsync(client, config.SiteUrl, cancellationToken);
            if (siteId is null)
                return new TestConnectionResponse
                {
                    Success = false,
                    Message = "Could not resolve SharePoint site. Verify the site URL and app permissions.",
                };

            return new TestConnectionResponse
            {
                Success = true,
                Message = $"Successfully connected to SharePoint site (siteId={siteId}).",
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "SharePoint test connection failed");
            return new TestConnectionResponse
            {
                Success = false,
                Message = "Failed to connect to SharePoint via Microsoft Graph.",
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
            var accessToken = await AcquireTokenAsync(config.EntraIdTenantId, config.ClientId, secretValue, cancellationToken);
            using var client = CreateGraphClient(accessToken);

            var siteId = await ResolveSiteIdAsync(client, config.SiteUrl, cancellationToken);
            if (siteId is null) return [];

            var drives = await ResolveDrivesAsync(client, siteId, config, cancellationToken);
            if (drives.Count == 0) return [];

            var records = new List<CanonicalRecord>();

            foreach (var drive in drives)
            {
                if (records.Count >= sampleSize) break;

                var items = await FetchDriveItemsAsync(
                    client, drive, config, tenantId, deltaLink: null,
                    top: sampleSize - records.Count, cancellationToken);
                records.AddRange(items.Records);
            }

            return records.Take(sampleSize).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Preview failed for SharePoint connector (tenant={TenantId})", tenantId);
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

        string accessToken;
        try
        {
            accessToken = await AcquireTokenAsync(config.EntraIdTenantId, config.ClientId, secretValue, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to acquire SharePoint access token");
            return ErrorResult($"Failed to acquire access token: {ex.Message}");
        }

        using var client = CreateGraphClient(accessToken);

        var siteId = await ResolveSiteIdAsync(client, config.SiteUrl, cancellationToken);
        if (siteId is null)
            return ErrorResult("Could not resolve SharePoint site.");

        var parsedCheckpoint = SharePointCheckpoint.Parse(checkpoint);
        var drives = await ResolveDrivesAsync(client, siteId, config, cancellationToken);
        if (drives.Count == 0)
            return ErrorResult("No accessible document libraries found.");

        var records = new List<CanonicalRecord>();
        var errors = new List<string>();
        var failedRecords = 0;
        string? lastDeltaLink = parsedCheckpoint?.DeltaLink;

        var startDriveIndex = parsedCheckpoint?.DriveIndex ?? 0;

        for (var di = startDriveIndex; di < drives.Count; di++)
        {
            var drive = drives[di];

            // Use delta link from checkpoint only for the starting drive (not subsequent ones).
            var deltaLink = (di == startDriveIndex && !isBackfill)
                ? parsedCheckpoint?.DeltaLink
                : null;

            try
            {
                var fetchResult = await FetchDriveItemsAsync(
                    client, drive, config, tenantId, deltaLink,
                    config.BatchSize, cancellationToken);

                records.AddRange(fetchResult.Records);
                errors.AddRange(fetchResult.Errors);
                failedRecords += fetchResult.FailedCount;

                // Track latest delta link for checkpoint.
                if (fetchResult.NextDeltaLink is not null)
                    lastDeltaLink = fetchResult.NextDeltaLink;

                // Yield if batch size hit — checkpoint at current drive with new delta link.
                if (records.Count >= config.BatchSize)
                {
                    var hasMoreInDrive = fetchResult.NextDeltaLink != fetchResult.CurrentDeltaLink;
                    var newCp = new SharePointCheckpoint(di, lastDeltaLink);
                    return new FetchResult
                    {
                        Records = records,
                        FailedRecords = failedRecords,
                        Errors = errors,
                        NewCheckpoint = newCp.Serialize(),
                        HasMore = hasMoreInDrive || di + 1 < drives.Count,
                    };
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to fetch from drive {DriveId} in site {SiteId}", drive.Id, siteId);
                errors.Add($"Drive '{drive.Name}' fetch failed: {ex.Message}");
                failedRecords++;
            }
        }

        // All drives processed — final batch.
        var finalCp = new SharePointCheckpoint(0, lastDeltaLink);

        return new FetchResult
        {
            Records = records,
            FailedRecords = failedRecords,
            Errors = errors,
            NewCheckpoint = finalCp.Serialize(),
            HasMore = false,
        };
    }

    // --- Drive Items ---

    internal async Task<DriveFetchResult> FetchDriveItemsAsync(
        HttpClient client, GraphDrive drive, SharePointSourceConfig config,
        string tenantId, string? deltaLink, int top, CancellationToken ct)
    {
        var records = new List<CanonicalRecord>();
        var errors = new List<string>();
        var failedCount = 0;

        // Build delta query URL.
        string? url;
        if (!string.IsNullOrEmpty(deltaLink))
        {
            url = deltaLink;
        }
        else
        {
            url = $"{GraphBaseUrl}/drives/{drive.Id}/root/delta?$top={top}&$select=id,name,file,folder,parentReference,webUrl,lastModifiedDateTime,createdDateTime,lastModifiedBy,size,deleted";
        }

        string? nextDeltaLink = deltaLink;
        var fetched = 0;

        while (url is not null && fetched < top)
        {
            HttpResponseMessage response;
            try
            {
                response = await client.GetAsync(url, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Delta query failed for drive {DriveName}", drive.Name);
                errors.Add($"Delta query failed for drive '{drive.Name}': {ex.Message}");
                break;
            }

            // Handle 410 Gone — delta token expired, reset to full sync.
            if (response.StatusCode == System.Net.HttpStatusCode.Gone)
            {
                _logger.LogWarning("Delta token expired for drive {DriveId}. Resetting to full sync.", drive.Id);
                url = $"{GraphBaseUrl}/drives/{drive.Id}/root/delta?$top={top}&$select=id,name,file,folder,parentReference,webUrl,lastModifiedDateTime,createdDateTime,lastModifiedBy,size,deleted";
                nextDeltaLink = null;
                continue;
            }

            response.EnsureSuccessStatusCode();

            var result = await DeserializeAsync<GraphDeltaResponse>(response, ct);
            if (result is null) break;

            foreach (var item in result.Value ?? [])
            {
                // Skip folders — we only ingest files.
                if (item.Folder is not null) continue;

                // Skip deleted items (they'll be handled via content hash dedup).
                if (item.Deleted is not null) continue;

                // Skip items without file metadata.
                if (item.File is null) continue;

                // Filter by extension.
                var extension = Path.GetExtension(item.Name);
                if (!IsExtensionSupported(extension, config))
                    continue;

                // Filter by excluded folders.
                if (IsExcludedFolder(item, config))
                    continue;

                try
                {
                    var record = MapDriveItemToCanonical(item, drive, config, tenantId);
                    if (record is not null)
                    {
                        // Download file content and extract text.
                        record = await DownloadAndExtractTextAsync(
                            client, drive.Id!, item, record, ct);
                        records.Add(record);
                        fetched++;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Failed to map drive item {ItemId} in drive {DriveId}", item.Id, drive.Id);
                    errors.Add($"Failed to map item '{item.Name}': {ex.Message}");
                    failedCount++;
                }
            }

            // Follow @odata.nextLink for pagination, or capture @odata.deltaLink for checkpoint.
            if (result.OdataNextLink is not null)
            {
                url = result.OdataNextLink;
            }
            else
            {
                nextDeltaLink = result.OdataDeltaLink ?? nextDeltaLink;
                url = null; // No more pages.
            }
        }

        return new DriveFetchResult(records, errors, failedCount, nextDeltaLink, deltaLink);
    }

    private static CanonicalRecord? MapDriveItemToCanonical(
        GraphDriveItem item, GraphDrive drive, SharePointSourceConfig config, string tenantId)
    {
        if (item.Id is null || item.Name is null) return null;

        var title = Path.GetFileNameWithoutExtension(item.Name);
        var deepLink = item.WebUrl ?? "";
        var extension = Path.GetExtension(item.Name).ToLowerInvariant();

        // Build path for context.
        var parentPath = item.ParentReference?.Path;
        var folderPath = parentPath is not null
            ? parentPath.Contains(":/") ? parentPath[(parentPath.IndexOf(":/") + 2)..] : parentPath
            : "";

        var author = item.LastModifiedBy?.User?.DisplayName;

        var createdAt = item.CreatedDateTime ?? DateTimeOffset.UtcNow;
        var updatedAt = item.LastModifiedDateTime ?? DateTimeOffset.UtcNow;

        // Content hash: based on item metadata since we don't download file content during ingestion.
        // Full text extraction happens in P0-010 (normalization/chunking pipeline).
        var contentForHash = $"{item.Id}|{item.Name}|{updatedAt:O}|{item.Size}";
        var contentHash = ComputeHash(contentForHash);

        // ACL mapping: SharePoint document libraries are scoped by site.
        // Use drive name as the access group. Items in shared drives are Internal by default.
        var driveName = drive.Name ?? "Documents";
        var allowedGroups = new List<string> { driveName };

        return new CanonicalRecord
        {
            TenantId = tenantId,
            EvidenceId = $"sp-{drive.Id}-{item.Id}",
            SourceSystem = ConnectorType.SharePoint,
            SourceType = MapFileExtensionToSourceType(extension),
            SourceLocator = new SourceLocator(item.Id, deepLink),
            Title = title,
            TextContent = $"[SharePoint document: {item.Name}] Path: {folderPath}/{item.Name}. Size: {item.Size} bytes. Type: {item.File?.MimeType ?? extension}.",
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            Status = EvidenceStatus.Open,
            Permissions = new RecordPermissions(AccessVisibility.Restricted, allowedGroups),
            ContentHash = contentHash,
            AccessLabel = $"Restricted ({driveName})",
            Author = author,
            ProductArea = folderPath,
            Tags = string.IsNullOrEmpty(extension) ? [] : [extension],
        };
    }

    // --- File Content Download + Text Extraction ---

    /// <summary>
    /// Downloads file content from Graph API and extracts text.
    /// For binary formats (PDF, DOCX, PPTX, XLSX): uses ITextExtractionService.
    /// For text formats (TXT, MD, CSV, etc.): reads content directly as UTF-8.
    /// Falls back to metadata-only TextContent on any failure.
    /// </summary>
    internal async Task<CanonicalRecord> DownloadAndExtractTextAsync(
        HttpClient client, string driveId, GraphDriveItem item,
        CanonicalRecord record, CancellationToken ct)
    {
        var extension = Path.GetExtension(item.Name ?? "").ToLowerInvariant();

        // Skip unsupported formats — keep metadata-only TextContent.
        if (!BinaryExtensions.Contains(extension) && !TextExtensions.Contains(extension))
            return record;

        try
        {
            var downloadUrl = $"{GraphBaseUrl}/drives/{driveId}/items/{item.Id}/content";
            using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Failed to download content for {ItemName} (HTTP {StatusCode})",
                    item.Name, (int)response.StatusCode);
                return record;
            }

            if (BinaryExtensions.Contains(extension))
            {
                // Binary extraction via PdfPig / Open XML SDK.
                using var stream = await response.Content.ReadAsStreamAsync(ct);

                // Buffer to MemoryStream so extraction libraries can seek.
                using var memoryStream = new MemoryStream();
                await stream.CopyToAsync(memoryStream, ct);
                memoryStream.Position = 0;

                var result = await _textExtractor.ExtractTextAsync(memoryStream, item.Name!, ct);
                if (result.Success && !string.IsNullOrWhiteSpace(result.Text))
                {
                    _logger.LogDebug(
                        "Extracted {Length} chars from {ItemName} ({Format}, {PageCount} pages/sheets)",
                        result.Text.Length, item.Name, result.Format, result.PageCount);

                    return record with { TextContent = result.Text };
                }

                _logger.LogDebug(
                    "Text extraction returned no content for {ItemName}: {Error}",
                    item.Name, result.Error);
                return record;
            }
            else
            {
                // Text-based file — read content directly.
                var text = await response.Content.ReadAsStringAsync(ct);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    _logger.LogDebug(
                        "Downloaded {Length} chars of text content from {ItemName}",
                        text.Length, item.Name);
                    return record with { TextContent = text };
                }

                return record;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Content download/extraction failed for {ItemName}. Using metadata-only.", item.Name);
            return record;
        }
    }

    // --- OAuth2 Token Acquisition ---

    internal async Task<string> AcquireTokenAsync(
        string entraIdTenantId, string clientId, string clientSecret,
        CancellationToken ct)
    {
        using var client = _httpClientFactory.CreateClient("SharePoint");

        var tokenUrl = string.Format(GraphTokenUrl, entraIdTenantId);
        using var requestBody = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["scope"] = "https://graph.microsoft.com/.default",
        });

        var response = await client.PostAsync(tokenUrl, requestBody, ct);
        response.EnsureSuccessStatusCode();

        var tokenResponse = await DeserializeAsync<OAuthTokenResponse>(response, ct);
        if (tokenResponse?.AccessToken is null)
            throw new InvalidOperationException("Failed to acquire access token: empty response.");

        return tokenResponse.AccessToken;
    }

    // --- Site and Drive Resolution ---

    internal async Task<string?> ResolveSiteIdAsync(HttpClient client, string siteUrl, CancellationToken ct)
    {
        // Parse site URL to extract hostname and site path.
        // e.g. "https://contoso.sharepoint.com/sites/support" → hostname=contoso.sharepoint.com, path=/sites/support
        if (!Uri.TryCreate(siteUrl, UriKind.Absolute, out var uri))
            return null;

        var hostname = uri.Host;
        var sitePath = uri.AbsolutePath.TrimEnd('/');

        if (string.IsNullOrEmpty(sitePath) || sitePath == "/")
        {
            // Root site.
            var url = $"{GraphBaseUrl}/sites/{hostname}";
            var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return null;
            var site = await DeserializeAsync<GraphSite>(response, ct);
            return site?.Id;
        }
        else
        {
            var url = $"{GraphBaseUrl}/sites/{hostname}:{sitePath}";
            var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return null;
            var site = await DeserializeAsync<GraphSite>(response, ct);
            return site?.Id;
        }
    }

    internal async Task<List<GraphDrive>> ResolveDrivesAsync(
        HttpClient client, string siteId, SharePointSourceConfig config, CancellationToken ct)
    {
        if (config.DriveIds.Count > 0)
        {
            // Use specific drives.
            var drives = new List<GraphDrive>();
            foreach (var driveId in config.DriveIds)
            {
                try
                {
                    var url = $"{GraphBaseUrl}/drives/{driveId}";
                    var response = await client.GetAsync(url, ct);
                    if (response.IsSuccessStatusCode)
                    {
                        var drive = await DeserializeAsync<GraphDrive>(response, ct);
                        if (drive is not null)
                            drives.Add(drive);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Failed to resolve drive {DriveId}", driveId);
                }
            }
            return drives;
        }

        // Discover all document libraries for the site.
        var listUrl = $"{GraphBaseUrl}/sites/{siteId}/drives";
        var listResponse = await client.GetAsync(listUrl, ct);
        if (!listResponse.IsSuccessStatusCode) return [];

        var result = await DeserializeAsync<GraphDriveListResponse>(listResponse, ct);
        return result?.Value?.ToList() ?? [];
    }

    // --- Helpers ---

    internal HttpClient CreateGraphClient(string accessToken)
    {
        var client = _httpClientFactory.CreateClient("SharePoint");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    internal static SharePointSourceConfig? ParseSourceConfig(string? json, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<SharePointSourceConfig>(json, SharedJsonOptions.CamelCaseIgnoreNull);
        }
        catch (JsonException ex)
        {
            logger?.LogWarning(ex, "Failed to deserialize SharePointSourceConfig from JSON");
            return null;
        }
    }

    private static bool IsExtensionSupported(string? extension, SharePointSourceConfig config)
    {
        if (string.IsNullOrEmpty(extension)) return false;

        if (config.IncludeExtensions.Count > 0)
            return config.IncludeExtensions.Any(e => e.Equals(extension, StringComparison.OrdinalIgnoreCase));

        return SupportedExtensions.Contains(extension);
    }

    private static bool IsExcludedFolder(GraphDriveItem item, SharePointSourceConfig config)
    {
        if (config.ExcludeFolders.Count == 0) return false;

        var parentPath = item.ParentReference?.Path;
        if (parentPath is null) return false;

        // Extract relative path after "root:".
        var colonIndex = parentPath.IndexOf(":/");
        var relativePath = colonIndex >= 0 ? parentPath[(colonIndex + 2)..] : parentPath;

        return config.ExcludeFolders.Any(f =>
            relativePath.StartsWith(f, StringComparison.OrdinalIgnoreCase) ||
            relativePath.Equals(f.TrimStart('/'), StringComparison.OrdinalIgnoreCase));
    }

    private static SourceType MapFileExtensionToSourceType(string extension) => extension switch
    {
        ".md" or ".txt" or ".rtf" => SourceType.Document,
        ".docx" or ".doc" or ".pdf" or ".odt" => SourceType.Document,
        ".pptx" or ".ppt" or ".odp" => SourceType.Document,
        ".xlsx" or ".xls" or ".ods" or ".csv" => SourceType.Document,
        ".html" or ".htm" => SourceType.WikiPage,
        _ => SourceType.Document,
    };

    internal static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static async Task<T?> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync<T>(stream, SharedJsonOptions.CamelCaseIgnoreNull, ct);
    }

    private static FetchResult ErrorResult(string error) => new()
    {
        Records = [],
        FailedRecords = 0,
        Errors = [error],
        HasMore = false,
    };

    // --- Graph API response models ---

    internal sealed class OAuthTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }

    internal sealed class GraphSite
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("webUrl")]
        public string? WebUrl { get; set; }
    }

    internal sealed class GraphDriveListResponse
    {
        [JsonPropertyName("value")]
        public List<GraphDrive>? Value { get; set; }
    }

    internal sealed class GraphDeltaResponse
    {
        [JsonPropertyName("value")]
        public List<GraphDriveItem>? Value { get; set; }

        [JsonPropertyName("@odata.nextLink")]
        public string? OdataNextLink { get; set; }

        [JsonPropertyName("@odata.deltaLink")]
        public string? OdataDeltaLink { get; set; }
    }

    // --- Internal result type ---

    internal sealed record DriveFetchResult(
        List<CanonicalRecord> Records,
        List<string> Errors,
        int FailedCount,
        string? NextDeltaLink,
        string? CurrentDeltaLink);
}

// --- Shared Graph models ---

public sealed class GraphDrive
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("driveType")]
    public string? DriveType { get; set; }

    [JsonPropertyName("webUrl")]
    public string? WebUrl { get; set; }
}

public sealed class GraphDriveItem
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("webUrl")]
    public string? WebUrl { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("createdDateTime")]
    public DateTimeOffset? CreatedDateTime { get; set; }

    [JsonPropertyName("lastModifiedDateTime")]
    public DateTimeOffset? LastModifiedDateTime { get; set; }

    [JsonPropertyName("file")]
    public GraphFileInfo? File { get; set; }

    [JsonPropertyName("folder")]
    public GraphFolderInfo? Folder { get; set; }

    [JsonPropertyName("deleted")]
    public GraphDeletedInfo? Deleted { get; set; }

    [JsonPropertyName("parentReference")]
    public GraphParentReference? ParentReference { get; set; }

    [JsonPropertyName("lastModifiedBy")]
    public GraphIdentitySet? LastModifiedBy { get; set; }
}

public sealed class GraphFileInfo
{
    [JsonPropertyName("mimeType")]
    public string? MimeType { get; set; }

    [JsonPropertyName("hashes")]
    public GraphHashInfo? Hashes { get; set; }
}

public sealed class GraphHashInfo
{
    [JsonPropertyName("quickXorHash")]
    public string? QuickXorHash { get; set; }

    [JsonPropertyName("sha256Hash")]
    public string? Sha256Hash { get; set; }
}

public sealed class GraphFolderInfo
{
    [JsonPropertyName("childCount")]
    public int ChildCount { get; set; }
}

public sealed class GraphDeletedInfo
{
    [JsonPropertyName("state")]
    public string? State { get; set; }
}

public sealed class GraphParentReference
{
    [JsonPropertyName("driveId")]
    public string? DriveId { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public sealed class GraphIdentitySet
{
    [JsonPropertyName("user")]
    public GraphIdentity? User { get; set; }

    [JsonPropertyName("application")]
    public GraphIdentity? Application { get; set; }
}

public sealed class GraphIdentity
{
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }
}

// --- Checkpoint model ---

/// <summary>
/// Tracks position across multi-drive SharePoint delta sync.
/// Serializes to a compact string for storage in SyncRunEntity.Checkpoint.
/// Format: "{driveIndex}|{deltaLink}"
/// Delta link is a full URL from Graph used for incremental sync.
/// Handles delta token expiry: if Graph returns 410 Gone, reset to full sync.
/// </summary>
internal sealed record SharePointCheckpoint(int DriveIndex, string? DeltaLink)
{
    public string Serialize()
    {
        return $"{DriveIndex}|{DeltaLink ?? ""}";
    }

    public static SharePointCheckpoint? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var pipeIndex = value.IndexOf('|');
        if (pipeIndex < 0) return null;

        if (!int.TryParse(value[..pipeIndex], out var driveIndex)) return null;

        var deltaLink = value[(pipeIndex + 1)..];
        return new SharePointCheckpoint(driveIndex, string.IsNullOrEmpty(deltaLink) ? null : deltaLink);
    }
}
