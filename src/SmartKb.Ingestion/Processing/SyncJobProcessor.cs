using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data;

namespace SmartKb.Ingestion.Processing;

/// <summary>
/// Processes a sync job: transitions SyncRun status, fetches records via the connector client,
/// persists checkpoint state, and surfaces field mapping failures.
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
    private readonly ILogger<SyncJobProcessor> _logger;

    public SyncJobProcessor(
        SmartKbDbContext db,
        IEnumerable<IConnectorClient> connectorClients,
        IAuditEventWriter auditWriter,
        ILogger<SyncJobProcessor> logger,
        ISecretProvider? secretProvider = null)
    {
        _db = db;
        _connectorClients = connectorClients;
        _auditWriter = auditWriter;
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
                $"Sync run {message.SyncRunId} completed: {totalProcessed} processed, {totalFailed} failed.");

            _logger.LogInformation(
                "Sync run {SyncRunId} completed: {Processed} processed, {Failed} failed",
                message.SyncRunId, totalProcessed, totalFailed);

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
