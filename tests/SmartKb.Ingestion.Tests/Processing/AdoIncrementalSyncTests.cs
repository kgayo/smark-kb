using System.Net;
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
/// Integration tests for ADO connector incremental sync (IsBackfill = false).
/// Verifies checkpoint usage and incremental fetch behavior.
/// </summary>
public class AdoIncrementalSyncTests : IDisposable
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

    public AdoIncrementalSyncTests()
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

        SeedTenant("tenant-inc");
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task ProcessAsync_IncrementalSync_CompletesSuccessfully()
    {
        // Arrange: incremental sync (IsBackfill = false) with work items.
        var handler = CreateWorkItemHandler(2);
        var adoClient = CreateAdoClient(handler);
        var secretProvider = new FakeSecretProvider();

        var connector = CreateConnector("tenant-inc", ingestWiki: false);
        connector.KeyVaultSecretName = "ado-pat";
        var syncRun = CreateSyncRun(connector, isBackfill: false);
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
        Assert.NotNull(updated.Checkpoint);
    }

    [Fact]
    public async Task ProcessAsync_IncrementalSync_UsesExistingCheckpoint()
    {
        // Arrange: incremental sync with a pre-existing checkpoint.
        var handler = CreateWorkItemHandler(1);
        var adoClient = CreateAdoClient(handler);
        var secretProvider = new FakeSecretProvider();

        var connector = CreateConnector("tenant-inc", ingestWiki: false);
        connector.KeyVaultSecretName = "ado-pat";
        var syncRun = CreateSyncRun(connector, isBackfill: false);
        syncRun.Checkpoint = "0|workitems|2026-03-01T00:00:00Z"; // Pre-existing checkpoint.
        await _db.SaveChangesAsync();

        var processor = new SyncJobProcessor(_db, [adoClient], _auditWriter, _pipeline, _processorLogger, secretProvider);
        var message = CreateMessage(syncRun, connector);

        // Act.
        var result = await processor.ProcessAsync(message, CancellationToken.None);

        // Assert: should complete and update checkpoint.
        Assert.True(result);

        var updated = await _db.SyncRuns.FirstAsync(s => s.Id == syncRun.Id);
        Assert.Equal(SyncRunStatus.Completed, updated.Status);
        Assert.NotNull(updated.Checkpoint);
        // Checkpoint should be updated (not the same as initial).
        Assert.NotEqual("0|workitems|2026-03-01T00:00:00Z", updated.Checkpoint);
    }

    [Fact]
    public async Task ProcessAsync_IncrementalSync_WritesAuditEvent()
    {
        // Arrange.
        var handler = CreateWorkItemHandler(1);
        var adoClient = CreateAdoClient(handler);
        var secretProvider = new FakeSecretProvider();

        var connector = CreateConnector("tenant-inc", ingestWiki: false);
        connector.KeyVaultSecretName = "ado-pat";
        var syncRun = CreateSyncRun(connector, isBackfill: false);
        await _db.SaveChangesAsync();

        var processor = new SyncJobProcessor(_db, [adoClient], _auditWriter, _pipeline, _processorLogger, secretProvider);
        var message = CreateMessage(syncRun, connector);

        // Act.
        await processor.ProcessAsync(message, CancellationToken.None);

        // Assert.
        Assert.Contains(_auditWriter.Events, e => e.EventType == AuditEventTypes.SyncCompleted);
    }

    [Fact]
    public async Task ProcessAsync_IncrementalSync_PersistsChunks()
    {
        // Arrange.
        var handler = CreateWorkItemHandler(2);
        var adoClient = CreateAdoClient(handler);
        var secretProvider = new FakeSecretProvider();

        var connector = CreateConnector("tenant-inc", ingestWiki: false);
        connector.KeyVaultSecretName = "ado-pat";
        var syncRun = CreateSyncRun(connector, isBackfill: false);
        await _db.SaveChangesAsync();

        var processor = new SyncJobProcessor(_db, [adoClient], _auditWriter, _pipeline, _processorLogger, secretProvider);
        var message = CreateMessage(syncRun, connector);

        // Act.
        await processor.ProcessAsync(message, CancellationToken.None);

        // Assert: chunks from ADO work items should be persisted.
        var chunks = await _db.EvidenceChunks.Where(c => c.TenantId == "tenant-inc").ToListAsync();
        Assert.NotEmpty(chunks);
    }

    // --- Helpers ---

    private void SeedTenant(string tenantId)
    {
        _db.Tenants.Add(new TenantEntity { TenantId = tenantId, DisplayName = "Incremental Test", IsActive = true });
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
            Name = "Test ADO Connector (Incremental)",
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

    private SyncRunEntity CreateSyncRun(ConnectorEntity connector, bool isBackfill = false, SyncRunStatus status = SyncRunStatus.Pending)
    {
        var entity = new SyncRunEntity
        {
            Id = Guid.NewGuid(),
            ConnectorId = connector.Id,
            TenantId = connector.TenantId,
            Status = status,
            IsBackfill = isBackfill,
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
            CorrelationId = $"test-inc-{Guid.NewGuid():N}",
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
}
