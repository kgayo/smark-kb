using SmartKb.Contracts.Models;

namespace SmartKb.Data.Entities;

/// <summary>
/// Extension methods for <see cref="ConnectorEntity"/> to reduce duplicate
/// SyncJobMessage construction across webhook handlers, polling fallback,
/// scheduled sync, and admin-triggered sync.
/// </summary>
public static class ConnectorEntityExtensions
{
    /// <summary>
    /// Creates a <see cref="SyncJobMessage"/> populated from this connector's
    /// current configuration.
    /// </summary>
    public static SyncJobMessage ToSyncJobMessage(
        this ConnectorEntity connector,
        Guid syncRunId,
        string? checkpoint,
        string correlationId,
        bool isBackfill = false,
        DateTimeOffset? enqueuedAt = null)
    {
        return new SyncJobMessage
        {
            SyncRunId = syncRunId,
            ConnectorId = connector.Id,
            TenantId = connector.TenantId,
            ConnectorType = connector.ConnectorType,
            IsBackfill = isBackfill,
            SourceConfig = connector.SourceConfig,
            FieldMapping = connector.FieldMapping,
            KeyVaultSecretName = connector.KeyVaultSecretName,
            AuthType = connector.AuthType,
            Checkpoint = checkpoint,
            CorrelationId = correlationId,
            EnqueuedAt = enqueuedAt ?? DateTimeOffset.UtcNow,
        };
    }
}
