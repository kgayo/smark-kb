using SmartKb.Contracts.Enums;

namespace SmartKb.Contracts.Models;

/// <summary>
/// Message published to Service Bus to trigger a sync job in the ingestion worker.
/// </summary>
public sealed record SyncJobMessage
{
    public required Guid SyncRunId { get; init; }
    public required Guid ConnectorId { get; init; }
    public required string TenantId { get; init; }
    public required ConnectorType ConnectorType { get; init; }
    public required bool IsBackfill { get; init; }
    public string? SourceConfig { get; init; }
    public string? FieldMapping { get; init; }
    public string? KeyVaultSecretName { get; init; }
    public SecretAuthType AuthType { get; init; }
    public string? Checkpoint { get; init; }
    public required string CorrelationId { get; init; }
    public required DateTimeOffset EnqueuedAt { get; init; }
}
