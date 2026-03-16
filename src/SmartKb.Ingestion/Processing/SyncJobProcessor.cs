using System.Text.Json;
using Microsoft.EntityFrameworkCore;
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
    private readonly IAuditEventWriter _auditWriter;
    private readonly INormalizationPipeline _pipeline;
    private readonly ILogger<SyncJobProcessor> _logger;

    public SyncJobProcessor(
        SmartKbDbContext db,
        IEnumerable<IConnectorClient> connectorClients,
        IAuditEventWriter auditWriter,
        INormalizationPipeline pipeline,
        ILogger<SyncJobProcessor> logger,
        ISecretProvider? secretProvider = null)
    {
        _db = db;
        _connectorClients = connectorClients;
        _auditWriter = auditWriter;
        _pipeline = pipeline;
        _logger = logger;
        _secretProvider = secretProvider;
    }

    public async Task<bool> ProcessAsync(SyncJobMessage message, CancellationToken cancellationToken)
    {
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
        if (!string.IsNullOrEmpty(message.KeyVaultSecretName) && _secretProvider is not null)
        {
            try
            {
                secretValue = await _secretProvider.GetSecretAsync(message.KeyVaultSecretName, cancellationToken);
            }
            catch (Exception ex)
            {
                await FailRunAsync(syncRun, $"Failed to retrieve credentials from Key Vault: {ex.Message}", cancellationToken);
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

                // Normalize: chunk + enrich records, then persist chunks.
                if (fetchResult.Records.Count > 0)
                {
                    var chunks = _pipeline.ProcessBatch(fetchResult.Records);
                    await PersistChunksAsync(chunks, message.ConnectorId, cancellationToken);
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

            await WriteAuditAsync(message, "sync.completed",
                $"Sync run {message.SyncRunId} completed: {totalProcessed} processed, {totalFailed} failed, {totalChunks} chunks produced.");

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
        catch (Exception ex)
        {
            await FailRunAsync(syncRun, ex.Message, cancellationToken);

            await WriteAuditAsync(message, "sync.failed",
                $"Sync run {message.SyncRunId} failed: {ex.Message}");

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

    private static FieldMappingConfig? DeserializeFieldMapping(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<FieldMappingConfig>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }
}
