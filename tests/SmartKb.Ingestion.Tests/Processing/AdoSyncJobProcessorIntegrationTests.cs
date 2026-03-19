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
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data;
using SmartKb.Data.Entities;
using SmartKb.Ingestion.Processing;

namespace SmartKb.Ingestion.Tests.Processing;

/// <summary>
/// Integration tests verifying that AzureDevOpsConnectorClient works correctly
/// when orchestrated by SyncJobProcessor (end-to-end sync run lifecycle).
/// </summary>
public class AdoSyncJobProcessorIntegrationTests : IDisposable
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

    public AdoSyncJobProcessorIntegrationTests()
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
            new EnhancedEnrichmentService(),
            new ChunkingSettings(),
            new LoggerFactory().CreateLogger<NormalizationPipeline>());

        SeedTenant("tenant-ado");
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task ProcessAsync_AdoWorkItems_CompletesWithRecords()
    {
        // Arrange: ADO returns 2 work items.
        var handler = CreateWorkItemHandler(2);
        var adoClient = CreateAdoClient(handler);
        var secretProvider = new FakeSecretProvider();

        var connector = CreateConnector("tenant-ado", ingestWiki: false);
        connector.KeyVaultSecretName = "ado-pat";
        var syncRun = CreateSyncRun(connector);
        await _db.SaveChangesAsync();

        var processor = new SyncJobProcessor(_db, [adoClient], _auditWriter, _pipeline, _processorLogger, secretProvider);
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
    public async Task ProcessAsync_AdoWikiPages_CompletesWithRecords()
    {
        // Arrange: ADO returns wiki pages.
        var handler = CreateWikiHandler();
        var adoClient = CreateAdoClient(handler);
        var secretProvider = new FakeSecretProvider();

        var connector = CreateConnector("tenant-ado", ingestWorkItems: false, ingestWiki: true);
        connector.KeyVaultSecretName = "ado-pat";
        var syncRun = CreateSyncRun(connector);
        await _db.SaveChangesAsync();

        var processor = new SyncJobProcessor(_db, [adoClient], _auditWriter, _pipeline, _processorLogger, secretProvider);
        var message = CreateMessage(syncRun, connector);

        // Act.
        var result = await processor.ProcessAsync(message, CancellationToken.None);

        // Assert.
        Assert.True(result);

        var updated = await _db.SyncRuns.FirstAsync(s => s.Id == syncRun.Id);
        Assert.Equal(SyncRunStatus.Completed, updated.Status);
        Assert.True(updated.RecordsProcessed > 0);
    }

    [Fact]
    public async Task ProcessAsync_AdoConnector_HandlesPerProjectAuthError()
    {
        // Arrange: ADO returns 401 on WIQL query (per-project error, not fatal).
        // The connector handles this gracefully by recording the error, not crashing the sync.
        var handler = new UniformMockHandler(HttpStatusCode.Unauthorized, "Access denied");
        var adoClient = CreateAdoClient(handler);
        var secretProvider = new FakeSecretProvider();

        var connector = CreateConnector("tenant-ado", ingestWiki: false);
        connector.KeyVaultSecretName = "ado-pat";
        var syncRun = CreateSyncRun(connector);
        await _db.SaveChangesAsync();

        var processor = new SyncJobProcessor(_db, [adoClient], _auditWriter, _pipeline, _processorLogger, secretProvider);
        var message = CreateMessage(syncRun, connector);

        // Act.
        var result = await processor.ProcessAsync(message, CancellationToken.None);

        // Assert: sync completes with 0 records and error detail (per-project failure).
        Assert.True(result);

        var updated = await _db.SyncRuns.FirstAsync(s => s.Id == syncRun.Id);
        Assert.Equal(SyncRunStatus.Completed, updated.Status);
        Assert.Equal(0, updated.RecordsProcessed);
        Assert.True(updated.RecordsFailed > 0);
        Assert.NotNull(updated.ErrorDetail);
    }

    [Fact]
    public async Task ProcessAsync_AdoConnector_FailsOnProjectsListAuthError()
    {
        // Arrange: ADO returns 401 on project list (fatal error - can't resolve projects).
        // Use empty Projects list to force the connector to call the projects API.
        var handler = new UniformMockHandler(HttpStatusCode.Unauthorized, "Access denied");
        var adoClient = CreateAdoClient(handler);
        var secretProvider = new FakeSecretProvider();

        var configNoProjects = new AzureDevOpsSourceConfig
        {
            OrganizationUrl = "https://dev.azure.com/testorg",
            Projects = [], // Forces projects list API call.
            IngestWorkItems = true,
            IngestWikiPages = false,
        };

        var connector = CreateConnector("tenant-ado", ingestWiki: false);
        connector.SourceConfig = JsonSerializer.Serialize(configNoProjects, JsonOptions);
        connector.KeyVaultSecretName = "ado-pat";
        var syncRun = CreateSyncRun(connector);
        await _db.SaveChangesAsync();

        var processor = new SyncJobProcessor(_db, [adoClient], _auditWriter, _pipeline, _processorLogger, secretProvider);
        var message = CreateMessage(syncRun, connector);

        // Act.
        var result = await processor.ProcessAsync(message, CancellationToken.None);

        // Assert: sync run fails because projects list is fatal.
        Assert.False(result);

        var updated = await _db.SyncRuns.FirstAsync(s => s.Id == syncRun.Id);
        Assert.Equal(SyncRunStatus.Failed, updated.Status);
    }

    [Fact]
    public async Task ProcessAsync_AdoConnector_WithSecretFromKeyVault()
    {
        // Arrange: PAT retrieved from Key Vault.
        var handler = CreateWorkItemHandler(1);
        var adoClient = CreateAdoClient(handler);
        var secretProvider = new FakeSecretProvider();

        var connector = CreateConnector("tenant-ado", ingestWiki: false);
        connector.KeyVaultSecretName = "ado-pat-secret";
        var syncRun = CreateSyncRun(connector);
        await _db.SaveChangesAsync();

        var processor = new SyncJobProcessor(_db, [adoClient], _auditWriter, _pipeline, _processorLogger, secretProvider);
        var message = CreateMessage(syncRun, connector) with { KeyVaultSecretName = "ado-pat-secret" };

        // Act.
        var result = await processor.ProcessAsync(message, CancellationToken.None);

        // Assert.
        Assert.True(result);

        var updated = await _db.SyncRuns.FirstAsync(s => s.Id == syncRun.Id);
        Assert.Equal(SyncRunStatus.Completed, updated.Status);
    }

    [Fact]
    public async Task ProcessAsync_AdoConnector_IdempotencySkipsDuplicate()
    {
        // Arrange: sync run already completed.
        var handler = CreateWorkItemHandler(1);
        var adoClient = CreateAdoClient(handler);

        var connector = CreateConnector("tenant-ado");
        var syncRun = CreateSyncRun(connector, SyncRunStatus.Completed);
        syncRun.CompletedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();

        var processor = new SyncJobProcessor(_db, [adoClient], _auditWriter, _pipeline, _processorLogger);
        var message = CreateMessage(syncRun, connector);

        // Act: replaying the message.
        var result = await processor.ProcessAsync(message, CancellationToken.None);

        // Assert: should skip without error.
        Assert.True(result);
    }

    [Fact]
    public async Task ProcessAsync_AdoConnector_CheckpointPreserved()
    {
        // Arrange.
        var handler = CreateWorkItemHandler(3);
        var adoClient = CreateAdoClient(handler);
        var secretProvider = new FakeSecretProvider();

        var connector = CreateConnector("tenant-ado", ingestWiki: false);
        connector.KeyVaultSecretName = "ado-pat";
        var syncRun = CreateSyncRun(connector);
        await _db.SaveChangesAsync();

        var processor = new SyncJobProcessor(_db, [adoClient], _auditWriter, _pipeline, _processorLogger, secretProvider);
        var message = CreateMessage(syncRun, connector);

        // Act.
        await processor.ProcessAsync(message, CancellationToken.None);

        // Assert: checkpoint should be persisted.
        var updated = await _db.SyncRuns.FirstAsync(s => s.Id == syncRun.Id);
        Assert.NotNull(updated.Checkpoint);
        Assert.Contains("|", updated.Checkpoint); // ADO checkpoint format: "index|phase|timestamp"
    }

    // --- Helpers ---

    private void SeedTenant(string tenantId)
    {
        _db.Tenants.Add(new TenantEntity { TenantId = tenantId, DisplayName = "ADO Test Tenant", IsActive = true });
        _db.SaveChanges();
    }

    private ConnectorEntity CreateConnector(string tenantId, bool ingestWorkItems = true, bool ingestWiki = true)
    {
        var config = new AzureDevOpsSourceConfig
        {
            OrganizationUrl = "https://dev.azure.com/testorg",
            Projects = ["TestProject"],
            IngestWorkItems = ingestWorkItems,
            IngestWikiPages = ingestWiki,
        };

        var entity = new ConnectorEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Test ADO Connector",
            ConnectorType = ConnectorType.AzureDevOps,
            Status = ConnectorStatus.Enabled,
            AuthType = SecretAuthType.Pat,
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
            ConnectorType = ConnectorType.AzureDevOps,
            IsBackfill = syncRun.IsBackfill,
            SourceConfig = connector.SourceConfig,
            FieldMapping = connector.FieldMapping,
            KeyVaultSecretName = connector.KeyVaultSecretName,
            AuthType = connector.AuthType,
            CorrelationId = $"test-corr-{Guid.NewGuid():N}",
            EnqueuedAt = DateTimeOffset.UtcNow,
        };
    }

    private static AzureDevOpsConnectorClient CreateAdoClient(HttpMessageHandler handler)
    {
        var factory = new TestHttpClientFactory(handler);
        var logger = new LoggerFactory().CreateLogger<AzureDevOpsConnectorClient>();
        return new AzureDevOpsConnectorClient(factory, logger);
    }

    private static AdoRoutingHandler CreateWorkItemHandler(int count)
    {
        var ids = Enumerable.Range(1, count).ToList();
        var wiqlItems = string.Join(",", ids.Select(id => $"{{\"id\":{id}}}"));
        var workItems = string.Join(",", ids.Select(id =>
            $"{{\"id\":{id},\"fields\":{{\"System.Title\":\"Item {id}\",\"System.Description\":\"Description for item {id}\",\"System.WorkItemType\":\"Bug\",\"System.State\":\"Active\",\"System.AreaPath\":\"TestProject\\\\Team\",\"System.CreatedDate\":\"2026-01-0{Math.Min(id, 9)}T00:00:00Z\",\"System.ChangedDate\":\"2026-03-0{Math.Min(id, 9)}T00:00:00Z\",\"System.Tags\":\"\"}}}}"));

        return new AdoRoutingHandler(new Dictionary<string, (HttpStatusCode, string)>
        {
            ["POST:_apis/wit/wiql"] = (HttpStatusCode.OK, $"{{\"workItems\":[{wiqlItems}]}}"),
            ["GET:_apis/wit/workitems?ids="] = (HttpStatusCode.OK, $"{{\"value\":[{workItems}]}}"),
        });
    }

    private static AdoRoutingHandler CreateWikiHandler()
    {
        return new AdoRoutingHandler(new Dictionary<string, (HttpStatusCode, string)>
        {
            ["GET:TestProject/_apis/wiki/wikis?api-version="] = (HttpStatusCode.OK,
                """{"value":[{"id":"wiki-1","name":"TestWiki"}]}"""),
            ["GET:TestProject/_apis/wiki/wikis/wiki-1/pages?api-version=7.1&recursionLevel=full"] = (HttpStatusCode.OK,
                """{"id":0,"path":"/","subPages":[{"id":1,"path":"/Page-One","subPages":[]}]}"""),
            ["GET:TestProject/_apis/wiki/wikis/wiki-1/pages?path=%2FPage-One"] = (HttpStatusCode.OK,
                """{"id":1,"path":"/Page-One","content":"Page one content here."}"""),
        });
    }
}

// --- Test infrastructure (scoped to this test file) ---

internal class TestHttpClientFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;
    public TestHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
    public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
}

internal class UniformMockHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _status;
    private readonly string _body;

    public UniformMockHandler(HttpStatusCode status, string body)
    {
        _status = status;
        _body = body;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new HttpResponseMessage(_status)
        {
            Content = new StringContent(_body, Encoding.UTF8, "application/json"),
        });
    }
}

internal class AdoRoutingHandler : HttpMessageHandler
{
    private readonly Dictionary<string, (HttpStatusCode Status, string Body)> _routes;

    public AdoRoutingHandler(Dictionary<string, (HttpStatusCode, string)> routes) => _routes = routes;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri?.PathAndQuery ?? "";
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
