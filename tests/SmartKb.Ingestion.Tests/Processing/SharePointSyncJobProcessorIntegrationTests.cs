using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Connectors;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Services;
using SmartKb.Contracts.Models;
using SmartKb.Data;
using SmartKb.Data.Entities;
using SmartKb.Ingestion.Processing;

namespace SmartKb.Ingestion.Tests.Processing;

/// <summary>
/// Integration tests verifying that SharePointConnectorClient works correctly
/// when orchestrated by SyncJobProcessor (end-to-end sync run lifecycle).
/// Mirrors AdoSyncJobProcessorIntegrationTests for the SharePoint connector.
/// </summary>
public class SharePointSyncJobProcessorIntegrationTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly SqliteConnection _connection;
    private readonly SmartKbDbContext _db;
    private readonly TestAuditWriter _auditWriter;
    private readonly ILogger<SyncJobProcessor> _processorLogger;
    private readonly INormalizationPipeline _pipeline;

    public SharePointSyncJobProcessorIntegrationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<SmartKbDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new SmartKbDbContext(options);
        _db.Database.EnsureCreated();

        _auditWriter = new TestAuditWriter();
        _processorLogger = new LoggerFactory().CreateLogger<SyncJobProcessor>();
        _pipeline = new NormalizationPipeline(
            new TextChunkingService(),
            new BaselineEnrichmentService(),
            new ChunkingSettings(),
            new LoggerFactory().CreateLogger<NormalizationPipeline>());

        SeedTenant("tenant-sp");
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task ProcessAsync_SharePointFiles_CompletesWithRecords()
    {
        // Arrange: Graph returns 2 document library files.
        var handler = CreateGraphHandler(fileCount: 2);
        var spClient = CreateSharePointClient(handler);
        var secretProvider = new FakeSecretProvider();

        var connector = CreateConnector("tenant-sp");
        connector.KeyVaultSecretName = "sp-client-secret";
        var syncRun = CreateSyncRun(connector);
        await _db.SaveChangesAsync();

        var processor = new SyncJobProcessor(_db, [spClient], _auditWriter, _pipeline, _processorLogger, secretProvider);
        var message = CreateMessage(syncRun, connector);

        // Act.
        var result = await processor.ProcessAsync(message, CancellationToken.None);

        // Assert.
        Assert.True(result);

        var updated = await _db.SyncRuns.FirstAsync(s => s.Id == syncRun.Id);
        Assert.Equal(SyncRunStatus.Completed, updated.Status);
        Assert.Equal(2, updated.RecordsProcessed);
        Assert.Equal(0, updated.RecordsFailed);
        Assert.NotNull(updated.Checkpoint);
        Assert.NotNull(updated.CompletedAt);

        Assert.Contains(_auditWriter.Events, e => e.EventType == AuditEventTypes.SyncCompleted);
    }

    [Fact]
    public async Task ProcessAsync_SharePointConnector_WithSecretFromKeyVault()
    {
        // Arrange: client secret retrieved from Key Vault.
        var handler = CreateGraphHandler(fileCount: 1);
        var spClient = CreateSharePointClient(handler);
        var secretProvider = new FakeSecretProvider();

        var connector = CreateConnector("tenant-sp");
        connector.KeyVaultSecretName = "sp-secret-key";
        var syncRun = CreateSyncRun(connector);
        await _db.SaveChangesAsync();

        var processor = new SyncJobProcessor(_db, [spClient], _auditWriter, _pipeline, _processorLogger, secretProvider);
        var message = CreateMessage(syncRun, connector) with { KeyVaultSecretName = "sp-secret-key" };

        // Act.
        var result = await processor.ProcessAsync(message, CancellationToken.None);

        // Assert.
        Assert.True(result);

        var updated = await _db.SyncRuns.FirstAsync(s => s.Id == syncRun.Id);
        Assert.Equal(SyncRunStatus.Completed, updated.Status);
    }

    [Fact]
    public async Task ProcessAsync_SharePointConnector_HandlesOAuthTokenFailure()
    {
        // Arrange: Graph returns 401 on token acquisition.
        // SharePoint connector catches OAuth exceptions and returns an error FetchResult
        // (not throwing), so SyncJobProcessor completes the run with 0 records and error detail.
        var handler = new UniformMockHandler(HttpStatusCode.Unauthorized, """{"error":"invalid_client"}""");
        var spClient = CreateSharePointClient(handler);
        var secretProvider = new FakeSecretProvider();

        var connector = CreateConnector("tenant-sp");
        connector.KeyVaultSecretName = "sp-secret";
        var syncRun = CreateSyncRun(connector);
        await _db.SaveChangesAsync();

        var processor = new SyncJobProcessor(_db, [spClient], _auditWriter, _pipeline, _processorLogger, secretProvider);
        var message = CreateMessage(syncRun, connector);

        // Act.
        var result = await processor.ProcessAsync(message, CancellationToken.None);

        // Assert: sync completes (connector returns error gracefully, not throwing).
        Assert.True(result);

        var updated = await _db.SyncRuns.FirstAsync(s => s.Id == syncRun.Id);
        Assert.Equal(SyncRunStatus.Completed, updated.Status);
        Assert.Equal(0, updated.RecordsProcessed);
        // Error detail should mention the token acquisition failure.
        Assert.NotNull(updated.ErrorDetail);
        Assert.Contains("token", updated.ErrorDetail!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProcessAsync_SharePointConnector_CheckpointPreserved()
    {
        // Arrange.
        var handler = CreateGraphHandler(fileCount: 3);
        var spClient = CreateSharePointClient(handler);
        var secretProvider = new FakeSecretProvider();

        var connector = CreateConnector("tenant-sp");
        connector.KeyVaultSecretName = "sp-secret";
        var syncRun = CreateSyncRun(connector);
        await _db.SaveChangesAsync();

        var processor = new SyncJobProcessor(_db, [spClient], _auditWriter, _pipeline, _processorLogger, secretProvider);
        var message = CreateMessage(syncRun, connector);

        // Act.
        await processor.ProcessAsync(message, CancellationToken.None);

        // Assert: checkpoint should be persisted (SharePoint format: "driveIndex|deltaLink").
        var updated = await _db.SyncRuns.FirstAsync(s => s.Id == syncRun.Id);
        Assert.NotNull(updated.Checkpoint);
        Assert.Contains("|", updated.Checkpoint); // SharePoint checkpoint format: "index|deltaLink"
    }

    [Fact]
    public async Task ProcessAsync_SharePointConnector_IdempotencySkipsDuplicate()
    {
        // Arrange: sync run already completed.
        var handler = CreateGraphHandler(fileCount: 1);
        var spClient = CreateSharePointClient(handler);

        var connector = CreateConnector("tenant-sp");
        var syncRun = CreateSyncRun(connector, SyncRunStatus.Completed);
        syncRun.CompletedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();

        var processor = new SyncJobProcessor(_db, [spClient], _auditWriter, _pipeline, _processorLogger);
        var message = CreateMessage(syncRun, connector);

        // Act: replaying the message.
        var result = await processor.ProcessAsync(message, CancellationToken.None);

        // Assert: should skip without error.
        Assert.True(result);
    }

    [Fact]
    public async Task ProcessAsync_SharePointConnector_PersistsChunks()
    {
        // Arrange.
        var handler = CreateGraphHandler(fileCount: 1);
        var spClient = CreateSharePointClient(handler);
        var secretProvider = new FakeSecretProvider();

        var connector = CreateConnector("tenant-sp");
        connector.KeyVaultSecretName = "sp-secret";
        var syncRun = CreateSyncRun(connector);
        await _db.SaveChangesAsync();

        var processor = new SyncJobProcessor(_db, [spClient], _auditWriter, _pipeline, _processorLogger, secretProvider);
        var message = CreateMessage(syncRun, connector);

        // Act.
        await processor.ProcessAsync(message, CancellationToken.None);

        // Assert: chunks should be persisted.
        var chunks = await _db.EvidenceChunks.Where(c => c.TenantId == "tenant-sp").ToListAsync();
        Assert.NotEmpty(chunks);
        Assert.All(chunks, c => Assert.StartsWith("sp-", c.EvidenceId));
    }

    // --- Helpers ---

    private void SeedTenant(string tenantId)
    {
        _db.Tenants.Add(new TenantEntity { TenantId = tenantId, DisplayName = "SP Test Tenant", IsActive = true });
        _db.SaveChanges();
    }

    private ConnectorEntity CreateConnector(string tenantId)
    {
        var config = new SharePointSourceConfig
        {
            SiteUrl = "https://contoso.sharepoint.com/sites/support",
            EntraIdTenantId = "00000000-0000-0000-0000-000000000001",
            ClientId = "00000000-0000-0000-0000-000000000002",
            DriveIds = ["drive-1"],
        };

        var entity = new ConnectorEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Test SP Connector",
            ConnectorType = ConnectorType.SharePoint,
            Status = ConnectorStatus.Enabled,
            AuthType = SecretAuthType.OAuth,
            SourceConfig = JsonSerializer.Serialize(config, JsonOptions),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.Connectors.Add(entity);
        return entity;
    }

    private SyncRunEntity CreateSyncRun(ConnectorEntity connector, SyncRunStatus status = SyncRunStatus.Pending)
    {
        var entity = new SyncRunEntity
        {
            Id = Guid.NewGuid(),
            ConnectorId = connector.Id,
            TenantId = connector.TenantId,
            Status = status,
            IsBackfill = true,
            StartedAt = DateTimeOffset.UtcNow,
            IdempotencyKey = Guid.NewGuid().ToString(),
        };
        _db.SyncRuns.Add(entity);
        return entity;
    }

    private SyncJobMessage CreateMessage(SyncRunEntity syncRun, ConnectorEntity connector)
    {
        return new SyncJobMessage
        {
            SyncRunId = syncRun.Id,
            ConnectorId = connector.Id,
            TenantId = connector.TenantId,
            ConnectorType = ConnectorType.SharePoint,
            IsBackfill = syncRun.IsBackfill,
            SourceConfig = connector.SourceConfig,
            FieldMapping = connector.FieldMapping,
            KeyVaultSecretName = connector.KeyVaultSecretName,
            AuthType = connector.AuthType,
            CorrelationId = $"test-corr-{Guid.NewGuid():N}",
            EnqueuedAt = DateTimeOffset.UtcNow,
        };
    }

    private static SharePointConnectorClient CreateSharePointClient(HttpMessageHandler handler)
    {
        var factory = new TestHttpClientFactory(handler);
        var logger = new LoggerFactory().CreateLogger<SharePointConnectorClient>();
        var extractor = new NullTextExtractionService();
        return new SharePointConnectorClient(factory, extractor, logger);
    }

    /// <summary>
    /// Creates an HTTP handler that mocks the Graph API endpoints needed for SharePoint ingestion:
    /// - OAuth token endpoint
    /// - Drive resolution (GET /drives/{id})
    /// - Delta query (GET /drives/{id}/root/delta)
    /// </summary>
    private static GraphRoutingHandler CreateGraphHandler(int fileCount)
    {
        var items = new StringBuilder();
        for (var i = 1; i <= fileCount; i++)
        {
            if (i > 1) items.Append(',');
            items.Append($$"""
            {
                "id": "file-{{i}}",
                "name": "document-{{i}}.txt",
                "webUrl": "https://contoso.sharepoint.com/sites/support/Shared Documents/document-{{i}}.txt",
                "size": {{100 + i * 50}},
                "createdDateTime": "2026-01-0{{Math.Min(i, 9)}}T00:00:00Z",
                "lastModifiedDateTime": "2026-03-0{{Math.Min(i, 9)}}T00:00:00Z",
                "file": { "mimeType": "text/plain" },
                "parentReference": { "driveId": "drive-1", "path": "/drive/root:" }
            }
            """);
        }

        return new GraphRoutingHandler(new Dictionary<string, (HttpStatusCode, string)>
        {
            // OAuth token response.
            ["POST:oauth2/v2.0/token"] = (HttpStatusCode.OK,
                """{"access_token":"test-access-token","token_type":"Bearer","expires_in":3600}"""),
            // Site resolution (Graph API: GET /sites/{hostname}:{sitePath}).
            ["GET:sites/contoso.sharepoint.com:/sites/support"] = (HttpStatusCode.OK,
                """{"id":"site-123","displayName":"Support","webUrl":"https://contoso.sharepoint.com/sites/support"}"""),
            // Drive resolution (specific drive by ID).
            ["GET:drives/drive-1"] = (HttpStatusCode.OK,
                """{"id":"drive-1","name":"Shared Documents","driveType":"documentLibrary","webUrl":"https://contoso.sharepoint.com/sites/support/Shared Documents"}"""),
            // Delta query.
            ["GET:drives/drive-1/root/delta"] = (HttpStatusCode.OK,
                $$"""{"value":[{{items}}],"@odata.deltaLink":"https://graph.microsoft.com/v1.0/drives/drive-1/root/delta?token=next-delta-token"}"""),
        });
    }
}

/// <summary>
/// Route-based HTTP mock handler for Microsoft Graph API calls.
/// Same pattern as AdoRoutingHandler but for Graph endpoints.
/// </summary>
internal class GraphRoutingHandler : HttpMessageHandler
{
    private readonly Dictionary<string, (HttpStatusCode Status, string Body)> _routes;

    public GraphRoutingHandler(Dictionary<string, (HttpStatusCode, string)> routes) => _routes = routes;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri?.ToString() ?? "";
        var method = request.Method.Method;

        (HttpStatusCode Status, string Body)? bestMatch = null;
        int bestLength = -1;

        foreach (var (key, value) in _routes)
        {
            var parts = key.Split(':', 2);
            if (parts.Length != 2) continue;

            if (method == parts[0] && url.Contains(parts[1]) && parts[1].Length > bestLength)
            {
                bestMatch = value;
                bestLength = parts[1].Length;
            }
        }

        if (bestMatch.HasValue)
        {
            return Task.FromResult(new HttpResponseMessage(bestMatch.Value.Status)
            {
                Content = new StringContent(bestMatch.Value.Body, Encoding.UTF8, "application/json"),
            });
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent($"No route for {method} {url}", Encoding.UTF8, "text/plain"),
        });
    }
}

internal sealed class NullTextExtractionService : ITextExtractionService
{
    public bool CanExtract(string fileName) => false;

    public Task<TextExtractionResult> ExtractTextAsync(
        Stream content, string fileName, CancellationToken cancellationToken = default)
        => Task.FromResult(TextExtractionResult.Failure("No extraction in test mode."));
}
