using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data;
using SmartKb.Data.Entities;
using SmartKb.Ingestion.Processing;

namespace SmartKb.Ingestion.Tests.Processing;

public class SyncJobProcessorTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SmartKbDbContext _db;
    private readonly TestAuditWriter _auditWriter;
    private readonly ILogger<SyncJobProcessor> _logger;
    private readonly INormalizationPipeline _pipeline;

    public SyncJobProcessorTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<SmartKbDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new SmartKbDbContext(options);
        _db.Database.EnsureCreated();

        _auditWriter = new TestAuditWriter();
        _logger = new LoggerFactory().CreateLogger<SyncJobProcessor>();
        _pipeline = new NormalizationPipeline(
            new TextChunkingService(),
            new BaselineEnrichmentService(),
            new ChunkingSettings(),
            new LoggerFactory().CreateLogger<NormalizationPipeline>());

        SeedTenant("tenant-1");
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task ProcessAsync_TransitionsPendingToRunningToCompleted()
    {
        var connector = CreateConnector("tenant-1");
        var syncRun = CreateSyncRun(connector, SyncRunStatus.Pending);
        await _db.SaveChangesAsync();

        var client = new FakeConnectorClient(new FetchResult
        {
            Records = [CreateRecord()],
            FailedRecords = 0,
            Errors = [],
            NewCheckpoint = "cp-1",
            HasMore = false,
        });

        var processor = CreateProcessor(client);
        var message = CreateMessage(syncRun, connector);

        var result = await processor.ProcessAsync(message, CancellationToken.None);

        Assert.True(result);

        var updated = await _db.SyncRuns.FirstAsync(s => s.Id == syncRun.Id);
        Assert.Equal(SyncRunStatus.Completed, updated.Status);
        Assert.Equal(1, updated.RecordsProcessed);
        Assert.Equal(0, updated.RecordsFailed);
        Assert.Equal("cp-1", updated.Checkpoint);
        Assert.NotNull(updated.CompletedAt);
    }

    [Fact]
    public async Task ProcessAsync_SkipsDuplicateForCompletedRun()
    {
        var connector = CreateConnector("tenant-1");
        var syncRun = CreateSyncRun(connector, SyncRunStatus.Completed);
        syncRun.CompletedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();

        var processor = CreateProcessor();
        var message = CreateMessage(syncRun, connector);

        var result = await processor.ProcessAsync(message, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task ProcessAsync_SkipsDuplicateForRunningRun()
    {
        var connector = CreateConnector("tenant-1");
        var syncRun = CreateSyncRun(connector, SyncRunStatus.Running);
        await _db.SaveChangesAsync();

        var processor = CreateProcessor();
        var message = CreateMessage(syncRun, connector);

        var result = await processor.ProcessAsync(message, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task ProcessAsync_ReturnsFalse_WhenSyncRunNotFound()
    {
        var processor = CreateProcessor();
        var message = new SyncJobMessage
        {
            SyncRunId = Guid.NewGuid(),
            ConnectorId = Guid.NewGuid(),
            TenantId = "tenant-1",
            ConnectorType = ConnectorType.AzureDevOps,
            IsBackfill = false,
            CorrelationId = "corr-1",
            EnqueuedAt = DateTimeOffset.UtcNow,
        };

        var result = await processor.ProcessAsync(message, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task ProcessAsync_ReturnsFalse_WhenNoConnectorClient()
    {
        var connector = CreateConnector("tenant-1");
        var syncRun = CreateSyncRun(connector, SyncRunStatus.Pending);
        await _db.SaveChangesAsync();

        // No connector clients registered.
        var processor = new SyncJobProcessor(_db, [], _auditWriter, _pipeline, _logger);
        var message = CreateMessage(syncRun, connector);

        var result = await processor.ProcessAsync(message, CancellationToken.None);

        Assert.False(result);

        var updated = await _db.SyncRuns.FirstAsync(s => s.Id == syncRun.Id);
        Assert.Equal(SyncRunStatus.Failed, updated.Status);
        Assert.Contains("No connector client", updated.ErrorDetail);
    }

    [Fact]
    public async Task ProcessAsync_FailsRun_WhenFetchThrows()
    {
        var connector = CreateConnector("tenant-1");
        var syncRun = CreateSyncRun(connector, SyncRunStatus.Pending);
        await _db.SaveChangesAsync();

        var client = new FakeConnectorClient(throwOnFetch: new InvalidOperationException("Source unavailable"));
        var processor = CreateProcessor(client);
        var message = CreateMessage(syncRun, connector);

        var result = await processor.ProcessAsync(message, CancellationToken.None);

        Assert.False(result);

        var updated = await _db.SyncRuns.FirstAsync(s => s.Id == syncRun.Id);
        Assert.Equal(SyncRunStatus.Failed, updated.Status);
        Assert.Contains("Source unavailable", updated.ErrorDetail);
    }

    [Fact]
    public async Task ProcessAsync_SurfacesFieldMappingErrors()
    {
        var connector = CreateConnector("tenant-1");
        var syncRun = CreateSyncRun(connector, SyncRunStatus.Pending);
        await _db.SaveChangesAsync();

        var client = new FakeConnectorClient(new FetchResult
        {
            Records = [CreateRecord()],
            FailedRecords = 2,
            Errors = ["Field 'Priority' has unexpected type", "Field 'Status' missing from source"],
            NewCheckpoint = "cp-2",
            HasMore = false,
        });

        var processor = CreateProcessor(client);
        var message = CreateMessage(syncRun, connector);

        var result = await processor.ProcessAsync(message, CancellationToken.None);

        Assert.True(result);

        var updated = await _db.SyncRuns.FirstAsync(s => s.Id == syncRun.Id);
        Assert.Equal(SyncRunStatus.Completed, updated.Status);
        Assert.Equal(1, updated.RecordsProcessed);
        Assert.Equal(2, updated.RecordsFailed);
        Assert.Contains("Priority", updated.ErrorDetail);
        Assert.Contains("Status", updated.ErrorDetail);
    }

    [Fact]
    public async Task ProcessAsync_HandlesMultipleBatches()
    {
        var connector = CreateConnector("tenant-1");
        var syncRun = CreateSyncRun(connector, SyncRunStatus.Pending);
        await _db.SaveChangesAsync();

        var batch1 = new FetchResult
        {
            Records = [CreateRecord(), CreateRecord()],
            FailedRecords = 0,
            Errors = [],
            NewCheckpoint = "cp-1",
            HasMore = true,
        };
        var batch2 = new FetchResult
        {
            Records = [CreateRecord()],
            FailedRecords = 1,
            Errors = ["Bad record"],
            NewCheckpoint = "cp-2",
            HasMore = false,
        };

        var client = new FakeConnectorClient(batch1, batch2);
        var processor = CreateProcessor(client);
        var message = CreateMessage(syncRun, connector);

        var result = await processor.ProcessAsync(message, CancellationToken.None);

        Assert.True(result);

        var updated = await _db.SyncRuns.FirstAsync(s => s.Id == syncRun.Id);
        Assert.Equal(SyncRunStatus.Completed, updated.Status);
        Assert.Equal(3, updated.RecordsProcessed);
        Assert.Equal(1, updated.RecordsFailed);
        Assert.Equal("cp-2", updated.Checkpoint);
    }

    [Fact]
    public async Task ProcessAsync_WritesAuditEvent_OnCompletion()
    {
        var connector = CreateConnector("tenant-1");
        var syncRun = CreateSyncRun(connector, SyncRunStatus.Pending);
        await _db.SaveChangesAsync();

        var client = new FakeConnectorClient(new FetchResult
        {
            Records = [],
            FailedRecords = 0,
            Errors = [],
            HasMore = false,
        });

        var processor = CreateProcessor(client);
        var message = CreateMessage(syncRun, connector);

        await processor.ProcessAsync(message, CancellationToken.None);

        Assert.Contains(_auditWriter.Events, e => e.EventType == AuditEventTypes.SyncCompleted);
    }

    [Fact]
    public async Task ProcessAsync_WritesAuditEvent_OnFailure()
    {
        var connector = CreateConnector("tenant-1");
        var syncRun = CreateSyncRun(connector, SyncRunStatus.Pending);
        await _db.SaveChangesAsync();

        var client = new FakeConnectorClient(throwOnFetch: new Exception("boom"));
        var processor = CreateProcessor(client);
        var message = CreateMessage(syncRun, connector);

        await processor.ProcessAsync(message, CancellationToken.None);

        Assert.Contains(_auditWriter.Events, e => e.EventType == AuditEventTypes.SyncFailed);
    }

    [Fact]
    public async Task ProcessAsync_FailsRun_WhenSecretRetrievalFails()
    {
        var connector = CreateConnector("tenant-1");
        connector.KeyVaultSecretName = "my-secret";
        var syncRun = CreateSyncRun(connector, SyncRunStatus.Pending);
        await _db.SaveChangesAsync();

        var client = new FakeConnectorClient(new FetchResult
        {
            Records = [],
            FailedRecords = 0,
            Errors = [],
            HasMore = false,
        });

        var secretProvider = new FakeSecretProvider(throwOnGet: new Exception("Vault down"));
        var processor = new SyncJobProcessor(_db, [client], _auditWriter, _pipeline, _logger, secretProvider);
        var message = CreateMessage(syncRun, connector);
        message = message with { KeyVaultSecretName = "my-secret" };

        var result = await processor.ProcessAsync(message, CancellationToken.None);

        Assert.False(result);

        var updated = await _db.SyncRuns.FirstAsync(s => s.Id == syncRun.Id);
        Assert.Equal(SyncRunStatus.Failed, updated.Status);
        Assert.Contains("Key Vault", updated.ErrorDetail);
    }

    [Fact]
    public async Task ProcessAsync_UploadsRawContent_WhenBlobStorageConfigured()
    {
        var connector = CreateConnector("tenant-1");
        var syncRun = CreateSyncRun(connector, SyncRunStatus.Pending);
        await _db.SaveChangesAsync();

        var record = CreateRecord();
        var client = new FakeConnectorClient(new FetchResult
        {
            Records = [record],
            FailedRecords = 0,
            Errors = [],
            NewCheckpoint = "cp-1",
            HasMore = false,
        });

        var blobStore = new FakeBlobStorageService();
        var processor = new SyncJobProcessor(_db, [client], _auditWriter, _pipeline, _logger, blobStorage: blobStore);
        var message = CreateMessage(syncRun, connector);

        var result = await processor.ProcessAsync(message, CancellationToken.None);

        Assert.True(result);
        Assert.Single(blobStore.Uploads);
        Assert.Equal(record.TextContent, blobStore.Uploads.First().Value);

        // Verify snapshot persisted in DB.
        var snapshot = await _db.RawContentSnapshots.FirstOrDefaultAsync(r => r.EvidenceId == record.EvidenceId);
        Assert.NotNull(snapshot);
        Assert.Equal(record.TenantId, snapshot.TenantId);
        Assert.Equal(connector.Id, snapshot.ConnectorId);
        Assert.Equal(record.ContentHash, snapshot.ContentHash);
        Assert.Contains("AzureDevOps", snapshot.BlobPath);
    }

    [Fact]
    public async Task ProcessAsync_SkipsBlobUpload_WhenContentHashUnchanged()
    {
        var connector = CreateConnector("tenant-1");
        var syncRun = CreateSyncRun(connector, SyncRunStatus.Pending);
        await _db.SaveChangesAsync();

        var record = CreateRecord();

        // Pre-seed a snapshot with matching content hash.
        _db.RawContentSnapshots.Add(new Data.Entities.RawContentSnapshotEntity
        {
            EvidenceId = record.EvidenceId,
            TenantId = record.TenantId,
            ConnectorId = connector.Id,
            BlobPath = $"{record.TenantId}/AzureDevOps/{record.EvidenceId}/raw",
            ContentHash = record.ContentHash,
            ContentLength = System.Text.Encoding.UTF8.GetByteCount(record.TextContent),
            ContentType = "text/plain; charset=utf-8",
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            UpdatedAt = DateTimeOffset.UtcNow.AddHours(-1),
        });
        await _db.SaveChangesAsync();

        var client = new FakeConnectorClient(new FetchResult
        {
            Records = [record],
            FailedRecords = 0,
            Errors = [],
            NewCheckpoint = "cp-1",
            HasMore = false,
        });

        var blobStore = new FakeBlobStorageService();
        var processor = new SyncJobProcessor(_db, [client], _auditWriter, _pipeline, _logger, blobStorage: blobStore);
        var message = CreateMessage(syncRun, connector);

        await processor.ProcessAsync(message, CancellationToken.None);

        // No upload should have happened since content hash is unchanged.
        Assert.Empty(blobStore.Uploads);
    }

    [Fact]
    public async Task ProcessAsync_ReUploadsBlob_WhenContentHashChanged()
    {
        var connector = CreateConnector("tenant-1");
        var syncRun = CreateSyncRun(connector, SyncRunStatus.Pending);
        await _db.SaveChangesAsync();

        var record = CreateRecord();

        // Pre-seed a snapshot with a DIFFERENT content hash.
        _db.RawContentSnapshots.Add(new Data.Entities.RawContentSnapshotEntity
        {
            EvidenceId = record.EvidenceId,
            TenantId = record.TenantId,
            ConnectorId = connector.Id,
            BlobPath = $"{record.TenantId}/AzureDevOps/{record.EvidenceId}/raw",
            ContentHash = "old-hash-different",
            ContentLength = 100,
            ContentType = "text/plain; charset=utf-8",
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            UpdatedAt = DateTimeOffset.UtcNow.AddHours(-1),
        });
        await _db.SaveChangesAsync();

        var client = new FakeConnectorClient(new FetchResult
        {
            Records = [record],
            FailedRecords = 0,
            Errors = [],
            NewCheckpoint = "cp-1",
            HasMore = false,
        });

        var blobStore = new FakeBlobStorageService();
        var processor = new SyncJobProcessor(_db, [client], _auditWriter, _pipeline, _logger, blobStorage: blobStore);
        var message = CreateMessage(syncRun, connector);

        await processor.ProcessAsync(message, CancellationToken.None);

        // Upload should have happened since content hash changed.
        Assert.Single(blobStore.Uploads);

        // Snapshot should be updated with new hash.
        var snapshot = await _db.RawContentSnapshots.FirstAsync(r => r.EvidenceId == record.EvidenceId);
        Assert.Equal(record.ContentHash, snapshot.ContentHash);
    }

    [Fact]
    public async Task ProcessAsync_WorksWithoutBlobStorage()
    {
        // Verify existing behavior: no blob storage configured = no crash.
        var connector = CreateConnector("tenant-1");
        var syncRun = CreateSyncRun(connector, SyncRunStatus.Pending);
        await _db.SaveChangesAsync();

        var client = new FakeConnectorClient(new FetchResult
        {
            Records = [CreateRecord()],
            FailedRecords = 0,
            Errors = [],
            NewCheckpoint = "cp-1",
            HasMore = false,
        });

        var processor = CreateProcessor(client);
        var message = CreateMessage(syncRun, connector);

        var result = await processor.ProcessAsync(message, CancellationToken.None);

        Assert.True(result);
        Assert.Empty(await _db.RawContentSnapshots.ToListAsync());
    }

    [Fact]
    public async Task ProcessAsync_IndexesChunks_WhenIndexingServiceConfigured()
    {
        var connector = CreateConnector("tenant-1");
        var syncRun = CreateSyncRun(connector, SyncRunStatus.Pending);
        await _db.SaveChangesAsync();

        var client = new FakeConnectorClient(new FetchResult
        {
            Records = [CreateRecord(), CreateRecord()],
            FailedRecords = 0,
            Errors = [],
            NewCheckpoint = "cp-1",
            HasMore = false,
        });

        var indexer = new FakeIndexingService();
        var processor = new SyncJobProcessor(_db, [client], _auditWriter, _pipeline, _logger, indexingService: indexer);
        var message = CreateMessage(syncRun, connector);

        var result = await processor.ProcessAsync(message, CancellationToken.None);

        Assert.True(result);
        Assert.Equal(1, indexer.IndexCallCount);
        Assert.True(indexer.TotalChunksIndexed > 0);
    }

    [Fact]
    public async Task ProcessAsync_CompletesSuccessfully_WhenIndexingFails()
    {
        var connector = CreateConnector("tenant-1");
        var syncRun = CreateSyncRun(connector, SyncRunStatus.Pending);
        await _db.SaveChangesAsync();

        var client = new FakeConnectorClient(new FetchResult
        {
            Records = [CreateRecord()],
            FailedRecords = 0,
            Errors = [],
            NewCheckpoint = "cp-1",
            HasMore = false,
        });

        var indexer = new FakeIndexingService(throwOnIndex: new InvalidOperationException("Search service down"));
        var processor = new SyncJobProcessor(_db, [client], _auditWriter, _pipeline, _logger, indexingService: indexer);
        var message = CreateMessage(syncRun, connector);

        // Indexing failure should not cause the sync run to fail.
        var result = await processor.ProcessAsync(message, CancellationToken.None);

        Assert.True(result);
        var updated = await _db.SyncRuns.FirstAsync(s => s.Id == syncRun.Id);
        Assert.Equal(SyncRunStatus.Completed, updated.Status);
    }

    [Fact]
    public async Task ProcessAsync_WorksWithoutIndexingService()
    {
        // Verify existing behavior: no indexing service configured = no crash.
        var connector = CreateConnector("tenant-1");
        var syncRun = CreateSyncRun(connector, SyncRunStatus.Pending);
        await _db.SaveChangesAsync();

        var client = new FakeConnectorClient(new FetchResult
        {
            Records = [CreateRecord()],
            FailedRecords = 0,
            Errors = [],
            NewCheckpoint = "cp-1",
            HasMore = false,
        });

        var processor = CreateProcessor(client);
        var message = CreateMessage(syncRun, connector);

        var result = await processor.ProcessAsync(message, CancellationToken.None);

        Assert.True(result);
    }

    // --- Helpers ---

    private void SeedTenant(string tenantId)
    {
        _db.Tenants.Add(new TenantEntity { TenantId = tenantId, DisplayName = "Test", IsActive = true });
        _db.SaveChanges();
    }

    private ConnectorEntity CreateConnector(string tenantId)
    {
        var entity = new ConnectorEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Test Connector",
            ConnectorType = ConnectorType.AzureDevOps,
            Status = ConnectorStatus.Enabled,
            AuthType = SecretAuthType.Pat,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.Connectors.Add(entity);
        return entity;
    }

    private SyncRunEntity CreateSyncRun(ConnectorEntity connector, SyncRunStatus status)
    {
        var entity = new SyncRunEntity
        {
            Id = Guid.NewGuid(),
            ConnectorId = connector.Id,
            TenantId = connector.TenantId,
            Status = status,
            IsBackfill = false,
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
            ConnectorType = connector.ConnectorType,
            IsBackfill = syncRun.IsBackfill,
            SourceConfig = connector.SourceConfig,
            FieldMapping = connector.FieldMapping,
            KeyVaultSecretName = connector.KeyVaultSecretName,
            AuthType = connector.AuthType,
            CorrelationId = "test-corr-id",
            EnqueuedAt = DateTimeOffset.UtcNow,
        };
    }

    private static CanonicalRecord CreateRecord()
    {
        return new CanonicalRecord
        {
            TenantId = "tenant-1",
            EvidenceId = Guid.NewGuid().ToString(),
            SourceSystem = ConnectorType.AzureDevOps,
            SourceType = SourceType.WorkItem,
            SourceLocator = new SourceLocator("123", "https://dev.azure.com/test"),
            Title = "Test Item",
            TextContent = "Test content",
            ContentHash = "abc123",
            AccessLabel = "Internal",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Status = EvidenceStatus.Open,
            Permissions = new RecordPermissions(AccessVisibility.Internal, []),
        };
    }

    private SyncJobProcessor CreateProcessor(params IConnectorClient[] clients)
    {
        return new SyncJobProcessor(_db, clients, _auditWriter, _pipeline, _logger);
    }
}

// --- Test doubles ---

internal class TestAuditWriter : IAuditEventWriter
{
    public List<AuditEvent> Events { get; } = [];

    public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        Events.Add(auditEvent);
        return Task.CompletedTask;
    }
}

internal class FakeConnectorClient : IConnectorClient
{
    private readonly Queue<FetchResult> _results = new();
    private readonly Exception? _throwOnFetch;

    public FakeConnectorClient(params FetchResult[] results)
    {
        foreach (var r in results) _results.Enqueue(r);
    }

    public FakeConnectorClient(Exception throwOnFetch)
    {
        _throwOnFetch = throwOnFetch;
    }

    // Convenience constructor for single exception.
    public FakeConnectorClient(FetchResult? ignored = null, Exception? throwOnFetch = null)
    {
        if (ignored is not null) _results.Enqueue(ignored);
        _throwOnFetch = throwOnFetch;
    }

    public ConnectorType Type => ConnectorType.AzureDevOps;

    public Task<TestConnectionResponse> TestConnectionAsync(string tenantId, string? sourceConfig, string? secretValue, CancellationToken cancellationToken = default)
        => Task.FromResult(new TestConnectionResponse { Success = true, Message = "OK" });

    public Task<IReadOnlyList<CanonicalRecord>> PreviewAsync(string tenantId, string? sourceConfig, FieldMappingConfig? fieldMapping, string? secretValue, int sampleSize, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<CanonicalRecord>>([]);

    public Task<FetchResult> FetchAsync(string tenantId, string? sourceConfig, FieldMappingConfig? fieldMapping, string? secretValue, string? checkpoint, bool isBackfill, CancellationToken cancellationToken = default)
    {
        if (_throwOnFetch is not null) throw _throwOnFetch;
        if (_results.Count == 0) return Task.FromResult(new FetchResult { Records = [], FailedRecords = 0, Errors = [], HasMore = false });
        return Task.FromResult(_results.Dequeue());
    }
}

internal class FakeBlobStorageService : IBlobStorageService
{
    public Dictionary<string, string> Uploads { get; } = new();

    public Task<string> UploadRawContentAsync(string tenantId, string connectorType, string evidenceId,
        string content, string contentType = "text/plain; charset=utf-8", CancellationToken cancellationToken = default)
    {
        var path = IBlobStorageService.BuildBlobPath(tenantId, connectorType, evidenceId);
        Uploads[path] = content;
        return Task.FromResult(path);
    }

    public Task<string?> DownloadRawContentAsync(string blobPath, CancellationToken cancellationToken = default)
        => Task.FromResult(Uploads.TryGetValue(blobPath, out var content) ? content : null);

    public Task<bool> DeleteRawContentAsync(string blobPath, CancellationToken cancellationToken = default)
        => Task.FromResult(Uploads.Remove(blobPath));

    public Task<bool> ExistsAsync(string blobPath, CancellationToken cancellationToken = default)
        => Task.FromResult(Uploads.ContainsKey(blobPath));
}

internal class FakeSecretProvider : ISecretProvider
{
    private readonly Exception? _throwOnGet;

    public FakeSecretProvider(Exception? throwOnGet = null) => _throwOnGet = throwOnGet;

    public Task<string> GetSecretAsync(string secretName, CancellationToken cancellationToken = default)
    {
        if (_throwOnGet is not null) throw _throwOnGet;
        return Task.FromResult("secret-value");
    }

    public Task SetSecretAsync(string secretName, string secretValue, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task DeleteSecretAsync(string secretName, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

internal class FakeIndexingService : IIndexingService
{
    private readonly Exception? _throwOnIndex;

    public int IndexCallCount { get; private set; }
    public int TotalChunksIndexed { get; private set; }

    public FakeIndexingService(Exception? throwOnIndex = null)
    {
        _throwOnIndex = throwOnIndex;
    }

    public Task EnsureIndexAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<IndexingResult> IndexChunksAsync(IReadOnlyList<EvidenceChunk> chunks, CancellationToken cancellationToken = default)
    {
        if (_throwOnIndex is not null) throw _throwOnIndex;
        IndexCallCount++;
        TotalChunksIndexed += chunks.Count;
        return Task.FromResult(new IndexingResult(chunks.Count, 0, []));
    }

    public Task<int> DeleteChunksAsync(IReadOnlyList<string> chunkIds, CancellationToken cancellationToken = default)
        => Task.FromResult(chunkIds.Count);
}
