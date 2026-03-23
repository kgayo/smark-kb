using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartKb.Contracts;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data;
using SmartKb.Data.Entities;

namespace SmartKb.Ingestion.Processing;

/// <summary>
/// Processes a sync job: transitions SyncRun status, fetches records via the connector client,
/// runs normalization (chunking + enrichment), persists chunks, and surfaces field mapping failures.
/// </summary>
public sealed class SyncJobProcessor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly SmartKbDbContext _db;
    private readonly IEnumerable<IConnectorClient> _connectorClients;
    private readonly ISecretProvider? _secretProvider;
    private readonly IOAuthTokenService? _oauthTokenService;
    private readonly IBlobStorageService? _blobStorage;
    private readonly IIndexingService? _indexingService;
    private readonly IRateLimitAlertService? _rateLimitAlertService;
    private readonly IRoutingTagResolver? _routingTagResolver;
    private readonly IAuditEventWriter _auditWriter;
    private readonly INormalizationPipeline _pipeline;
    private readonly ILogger<SyncJobProcessor> _logger;

    public SyncJobProcessor(
        SmartKbDbContext db,
        IEnumerable<IConnectorClient> connectorClients,
        IAuditEventWriter auditWriter,
        INormalizationPipeline pipeline,
        ILogger<SyncJobProcessor> logger,
        ISecretProvider? secretProvider = null,
        IOAuthTokenService? oauthTokenService = null,
        IBlobStorageService? blobStorage = null,
        IIndexingService? indexingService = null,
        IRateLimitAlertService? rateLimitAlertService = null,
        IRoutingTagResolver? routingTagResolver = null)
    {
        _db = db;
        _connectorClients = connectorClients;
        _auditWriter = auditWriter;
        _pipeline = pipeline;
        _logger = logger;
        _secretProvider = secretProvider;
        _oauthTokenService = oauthTokenService;
        _blobStorage = blobStorage;
        _indexingService = indexingService;
        _rateLimitAlertService = rateLimitAlertService;
        _routingTagResolver = routingTagResolver;
    }

    public async Task<bool> ProcessAsync(SyncJobMessage message, CancellationToken cancellationToken)
    {
        var syncSw = System.Diagnostics.Stopwatch.StartNew();
        using var activity = Diagnostics.IngestionSource.StartActivity("SyncJobProcess");
        activity?.SetTag("smartkb.sync_run_id", message.SyncRunId.ToString());
        activity?.SetTag("smartkb.connector_id", message.ConnectorId.ToString());
        activity?.SetTag("smartkb.tenant_id", message.TenantId);
        activity?.SetTag("smartkb.connector_type", message.ConnectorType.ToString());
        activity?.SetTag("smartkb.is_backfill", message.IsBackfill);

        var syncRun = await _db.SyncRuns
            .FirstOrDefaultAsync(s => s.Id == message.SyncRunId && s.TenantId == message.TenantId, cancellationToken);

        if (syncRun is null)
        {
            _logger.LogWarning("SyncRun {SyncRunId} not found for tenant {TenantId}. Abandoning message.",
                message.SyncRunId, message.TenantId);
            return false;
        }

        // Idempotency: skip if already completed or running.
        if (syncRun.Status is SyncRunStatus.Completed or SyncRunStatus.Running)
        {
            _logger.LogInformation("SyncRun {SyncRunId} already {Status}. Skipping duplicate.",
                message.SyncRunId, syncRun.Status);
            return true;
        }

        // Transition to Running.
        syncRun.Status = SyncRunStatus.Running;
        syncRun.StartedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Starting sync run {SyncRunId} for connector {ConnectorId} (tenant={TenantId}, type={ConnectorType}, backfill={IsBackfill})",
            message.SyncRunId, message.ConnectorId, message.TenantId, message.ConnectorType, message.IsBackfill);

        var client = _connectorClients.FirstOrDefault(c => c.Type == message.ConnectorType);
        if (client is null)
        {
            await FailRunAsync(syncRun, $"No connector client registered for type '{message.ConnectorType}'.", cancellationToken);
            return false;
        }

        string? secretValue = null;
        if (!string.IsNullOrEmpty(message.KeyVaultSecretName))
        {
            try
            {
                if (message.AuthType == SecretAuthType.OAuth && _oauthTokenService is not null)
                {
                    secretValue = await _oauthTokenService.ResolveAccessTokenAsync(
                        message.KeyVaultSecretName, message.SourceConfig, message.ConnectorType, cancellationToken);
                    if (secretValue is null)
                    {
                        await FailRunAsync(syncRun, "OAuth token resolution failed. The connector may need to be re-authorized.", cancellationToken);
                        return false;
                    }
                }
                else if (_secretProvider is not null)
                {
                    secretValue = await _secretProvider.GetSecretAsync(message.KeyVaultSecretName, cancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await FailRunAsync(syncRun, $"Failed to retrieve credentials: {ex.Message}", cancellationToken);
                return false;
            }
        }

        var fieldMapping = DeserializeFieldMapping(message.FieldMapping);
        var checkpoint = message.Checkpoint;
        var totalProcessed = 0;
        var totalFailed = 0;
        var totalChunks = 0;
        var allErrors = new List<string>();

        try
        {
            bool hasMore;
            do
            {
                var fetchResult = await client.FetchAsync(
                    message.TenantId,
                    message.SourceConfig,
                    fieldMapping,
                    secretValue,
                    checkpoint,
                    message.IsBackfill,
                    cancellationToken);

                totalProcessed += fetchResult.Records.Count;
                totalFailed += fetchResult.FailedRecords;

                if (fetchResult.Errors.Count > 0)
                {
                    allErrors.AddRange(fetchResult.Errors);
                    foreach (var error in fetchResult.Errors)
                    {
                        _logger.LogWarning(
                            "Field mapping error in sync run {SyncRunId}: {Error}",
                            message.SyncRunId, error);
                    }
                }

                // Upload raw content snapshots to blob storage, then normalize, persist, and index.
                if (fetchResult.Records.Count > 0)
                {
                    await UploadRawContentAsync(fetchResult.Records, message.ConnectorId, cancellationToken);

                    // Apply routing tag mappings before normalization so explicit admin mappings
                    // take precedence over keyword-based enrichment inference.
                    var recordsForNormalization = _routingTagResolver is not null
                        ? _routingTagResolver.ApplyRoutingTagsBatch(fetchResult.Records, fieldMapping)
                        : fetchResult.Records;

                    var chunks = _pipeline.ProcessBatch(recordsForNormalization);
                    await PersistChunksAsync(chunks, message.ConnectorId, cancellationToken);
                    await IndexChunksAsync(chunks, cancellationToken);
                    totalChunks += chunks.Count;
                }

                // Update checkpoint after each batch.
                if (fetchResult.NewCheckpoint is not null)
                {
                    checkpoint = fetchResult.NewCheckpoint;
                    syncRun.Checkpoint = checkpoint;
                    syncRun.RecordsProcessed = totalProcessed;
                    syncRun.RecordsFailed = totalFailed;
                    await _db.SaveChangesAsync(cancellationToken);
                }

                hasMore = fetchResult.HasMore;
            } while (hasMore && !cancellationToken.IsCancellationRequested);

            // Complete the run.
            syncRun.Status = SyncRunStatus.Completed;
            syncRun.CompletedAt = DateTimeOffset.UtcNow;
            syncRun.RecordsProcessed = totalProcessed;
            syncRun.RecordsFailed = totalFailed;
            if (allErrors.Count > 0)
            {
                syncRun.ErrorDetail = JsonSerializer.Serialize(allErrors.Take(50), JsonOptions);
            }
            await _db.SaveChangesAsync(cancellationToken);

            await WriteAuditAsync(message, AuditEventTypes.SyncCompleted,
                $"Sync run {message.SyncRunId} completed: {totalProcessed} processed, {totalFailed} failed, {totalChunks} chunks produced.");

            activity?.SetTag("smartkb.records_processed", totalProcessed);
            activity?.SetTag("smartkb.records_failed", totalFailed);
            activity?.SetTag("smartkb.chunks_produced", totalChunks);
            activity?.SetStatus(ActivityStatusCode.Ok);

            // P0-022: Record SLO metrics.
            syncSw.Stop();
            Diagnostics.SyncJobDurationMs.Record(syncSw.ElapsedMilliseconds,
                new KeyValuePair<string, object?>("smartkb.connector_type", message.ConnectorType.ToString()),
                new KeyValuePair<string, object?>("smartkb.tenant_id", message.TenantId));
            Diagnostics.SyncJobsCompletedTotal.Add(1,
                new KeyValuePair<string, object?>("smartkb.connector_type", message.ConnectorType.ToString()),
                new KeyValuePair<string, object?>("smartkb.tenant_id", message.TenantId));
            Diagnostics.RecordsProcessedTotal.Add(totalProcessed,
                new KeyValuePair<string, object?>("smartkb.connector_type", message.ConnectorType.ToString()),
                new KeyValuePair<string, object?>("smartkb.tenant_id", message.TenantId));

            _logger.LogInformation(
                "Sync run {SyncRunId} completed: {Processed} processed, {Failed} failed, {Chunks} chunks",
                message.SyncRunId, totalProcessed, totalFailed, totalChunks);

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Graceful shutdown — leave as Running so it can be retried.
            syncRun.RecordsProcessed = totalProcessed;
            syncRun.RecordsFailed = totalFailed;
            await _db.SaveChangesAsync(CancellationToken.None);

            _logger.LogWarning("Sync run {SyncRunId} interrupted by cancellation at {Processed} records.",
                message.SyncRunId, totalProcessed);
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await FailRunAsync(syncRun, ex.Message, cancellationToken);

            await WriteAuditAsync(message, AuditEventTypes.SyncFailed,
                $"Sync run {message.SyncRunId} failed: {ex.Message}");

            // P1-008 + P3-020: Track source API rate-limit hits (HTTP 429).
            if (ex is HttpRequestException hre && hre.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                Diagnostics.SourceRateLimitTotal.Add(1,
                    new KeyValuePair<string, object?>("smartkb.connector_type", message.ConnectorType.ToString()),
                    new KeyValuePair<string, object?>("smartkb.tenant_id", message.TenantId));

                // P3-020: Persist rate-limit event for diagnostics alerting.
                if (_rateLimitAlertService is not null)
                {
                    try
                    {
                        await _rateLimitAlertService.RecordRateLimitEventAsync(
                            message.TenantId, message.ConnectorId, message.ConnectorType.ToString(), cancellationToken);
                    }
                    catch (Exception rlEx)
                    {
                        _logger.LogWarning(rlEx, "Failed to record rate-limit event for connector {ConnectorId}.", message.ConnectorId);
                    }
                }
            }

            // P0-022: Record failure metrics.
            syncSw.Stop();
            Diagnostics.SyncJobDurationMs.Record(syncSw.ElapsedMilliseconds,
                new KeyValuePair<string, object?>("smartkb.connector_type", message.ConnectorType.ToString()),
                new KeyValuePair<string, object?>("smartkb.tenant_id", message.TenantId));
            Diagnostics.SyncJobsFailedTotal.Add(1,
                new KeyValuePair<string, object?>("smartkb.connector_type", message.ConnectorType.ToString()),
                new KeyValuePair<string, object?>("smartkb.tenant_id", message.TenantId));

            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return false;
        }
    }

    private async Task FailRunAsync(Data.Entities.SyncRunEntity syncRun, string error, CancellationToken ct)
    {
        syncRun.Status = SyncRunStatus.Failed;
        syncRun.CompletedAt = DateTimeOffset.UtcNow;
        syncRun.ErrorDetail = error.Length > 4000 ? error[..4000] : error;
        await _db.SaveChangesAsync(ct);

        _logger.LogError("Sync run {SyncRunId} failed: {Error}", syncRun.Id, error);
    }

    private async Task WriteAuditAsync(SyncJobMessage message, string eventType, string detail)
    {
        await _auditWriter.WriteAsync(new AuditEvent(
            EventId: Guid.NewGuid().ToString(),
            EventType: eventType,
            TenantId: message.TenantId,
            ActorId: "system:ingestion-worker",
            CorrelationId: message.CorrelationId,
            Timestamp: DateTimeOffset.UtcNow,
            Detail: detail));
    }

    private async Task UploadRawContentAsync(
        IReadOnlyList<CanonicalRecord> records,
        Guid connectorId,
        CancellationToken ct)
    {
        if (_blobStorage is null) return;

        var now = DateTimeOffset.UtcNow;
        foreach (var record in records)
        {
            var connectorType = record.SourceSystem.ToString();
            var blobPath = IBlobStorageService.BuildBlobPath(record.TenantId, connectorType, record.EvidenceId);

            // Skip upload if content hash hasn't changed.
            var existing = await _db.RawContentSnapshots
                .FirstOrDefaultAsync(r => r.EvidenceId == record.EvidenceId, ct);

            if (existing is not null && existing.ContentHash == record.ContentHash)
            {
                _logger.LogDebug("Raw content unchanged for {EvidenceId}, skipping blob upload.", record.EvidenceId);
                continue;
            }

            await _blobStorage.UploadRawContentAsync(
                record.TenantId, connectorType, record.EvidenceId, record.TextContent,
                cancellationToken: ct);

            var contentLength = System.Text.Encoding.UTF8.GetByteCount(record.TextContent);

            if (existing is not null)
            {
                existing.BlobPath = blobPath;
                existing.ContentHash = record.ContentHash;
                existing.ContentLength = contentLength;
                existing.UpdatedAt = now;
            }
            else
            {
                _db.RawContentSnapshots.Add(new RawContentSnapshotEntity
                {
                    EvidenceId = record.EvidenceId,
                    TenantId = record.TenantId,
                    ConnectorId = connectorId,
                    BlobPath = blobPath,
                    ContentHash = record.ContentHash,
                    ContentLength = contentLength,
                    ContentType = "text/plain; charset=utf-8",
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task PersistChunksAsync(
        IReadOnlyList<EvidenceChunk> chunks,
        Guid connectorId,
        CancellationToken ct)
    {
        if (chunks.Count == 0) return;

        var now = DateTimeOffset.UtcNow;
        foreach (var chunk in chunks)
        {
            // Upsert: if chunk already exists with same content hash, skip. Otherwise replace.
            var existing = await _db.EvidenceChunks
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.ChunkId == chunk.ChunkId, ct);

            var entity = new EvidenceChunkEntity
            {
                ChunkId = chunk.ChunkId,
                EvidenceId = chunk.EvidenceId,
                TenantId = chunk.TenantId,
                ConnectorId = connectorId,
                ChunkIndex = chunk.ChunkIndex,
                ChunkText = chunk.ChunkText,
                ChunkContext = chunk.ChunkContext,
                SourceSystem = chunk.SourceSystem.ToString(),
                SourceType = chunk.SourceType.ToString(),
                Status = chunk.Status.ToString(),
                UpdatedAt = chunk.UpdatedAt,
                ProductArea = chunk.ProductArea,
                Tags = chunk.Tags.Count > 0 ? JsonSerializer.Serialize(chunk.Tags, JsonOptions) : null,
                Visibility = chunk.Visibility.ToString(),
                AllowedGroups = chunk.AllowedGroups.Count > 0 ? JsonSerializer.Serialize(chunk.AllowedGroups, JsonOptions) : null,
                AccessLabel = chunk.AccessLabel,
                Title = chunk.Title,
                SourceUrl = chunk.SourceUrl,
                ErrorTokens = chunk.ErrorTokens.Count > 0 ? JsonSerializer.Serialize(chunk.ErrorTokens, JsonOptions) : null,
                EnrichmentVersion = chunk.EnrichmentVersion,
                ContentHash = ComputeChunkHash(chunk),
                CreatedAt = existing?.CreatedAt ?? now,
                ReprocessedAt = existing is not null ? now : null,
            };

            if (existing is not null)
            {
                _db.EvidenceChunks.Update(entity);
            }
            else
            {
                _db.EvidenceChunks.Add(entity);
            }
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogDebug("Persisted {Count} evidence chunks for connector {ConnectorId}.", chunks.Count, connectorId);
    }

    private static string ComputeChunkHash(EvidenceChunk chunk)
    {
        var input = $"{chunk.ChunkId}|{chunk.ChunkText}|{chunk.EnrichmentVersion}";
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private async Task IndexChunksAsync(
        IReadOnlyList<EvidenceChunk> chunks,
        CancellationToken ct)
    {
        if (_indexingService is null || chunks.Count == 0) return;

        try
        {
            var result = await _indexingService.IndexChunksAsync(chunks, ct);
            if (result.Failed > 0)
            {
                _logger.LogWarning(
                    "Indexing partially failed: {Succeeded} succeeded, {Failed} failed. Failed IDs: {FailedIds}",
                    result.Succeeded, result.Failed, string.Join(", ", result.FailedChunkIds.Take(10)));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Indexing failures are non-fatal — chunks are persisted in SQL and can be re-indexed.
            _logger.LogError(ex, "Failed to index {Count} chunks. They are persisted in SQL for retry.", chunks.Count);
        }
    }

    private FieldMappingConfig? DeserializeFieldMapping(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<FieldMappingConfig>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize FieldMappingConfig from JSON");
            return null;
        }
    }
}
