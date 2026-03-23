using SmartKb.Contracts.Enums;

namespace SmartKb.Contracts.Models;

// --- Request DTOs ---

public sealed record CreateConnectorRequest
{
    public required string Name { get; init; }
    public required ConnectorType ConnectorType { get; init; }
    public required SecretAuthType AuthType { get; init; }
    public string? KeyVaultSecretName { get; init; }
    public string? SourceConfig { get; init; }
    public FieldMappingConfig? FieldMapping { get; init; }
    public string? ScheduleCron { get; init; }
}

public sealed record UpdateConnectorRequest
{
    public string? Name { get; init; }
    public string? SourceConfig { get; init; }
    public FieldMappingConfig? FieldMapping { get; init; }
    public string? ScheduleCron { get; init; }
    public string? KeyVaultSecretName { get; init; }
    public SecretAuthType? AuthType { get; init; }
}

public sealed record SyncNowRequest
{
    public bool IsBackfill { get; init; }
    public string? IdempotencyKey { get; init; }
}

public sealed record PreviewRequest
{
    public FieldMappingConfig? FieldMapping { get; init; }
    public int SampleSize { get; init; } = 5;
}

public sealed record PreviewRetrievalRequest
{
    public required string Query { get; init; }
    public int MaxResults { get; init; } = 5;
}

public sealed record RotateSecretRequest
{
    public required string NewSecretValue { get; init; }
}

// --- Response DTOs ---

public sealed record ConnectorResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required ConnectorType ConnectorType { get; init; }
    public required ConnectorStatus Status { get; init; }
    public required SecretAuthType AuthType { get; init; }
    public required bool HasSecret { get; init; }
    public string? SourceConfig { get; init; }
    public FieldMappingConfig? FieldMapping { get; init; }
    public string? ScheduleCron { get; init; }
    public DateTimeOffset? LastScheduledSyncAt { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public SyncRunSummary? LastSyncRun { get; init; }
}

public sealed record ConnectorListResponse
{
    public required IReadOnlyList<ConnectorResponse> Connectors { get; init; }
    public required int TotalCount { get; init; }
}

public sealed record SyncRunSummary
{
    public required Guid Id { get; init; }
    public required SyncRunStatus Status { get; init; }
    public required bool IsBackfill { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public required int RecordsProcessed { get; init; }
    public required int RecordsFailed { get; init; }
    public string? ErrorDetail { get; init; }
}

public sealed record SyncRunListResponse
{
    public required IReadOnlyList<SyncRunSummary> SyncRuns { get; init; }
    public required int TotalCount { get; init; }
}

public sealed record TestConnectionResponse
{
    public required bool Success { get; init; }
    public required string Message { get; init; }
    public string? DiagnosticDetail { get; init; }
}

public sealed record PreviewResponse
{
    public required IReadOnlyList<CanonicalRecord> Records { get; init; }
    public required IReadOnlyList<string> ValidationErrors { get; init; }
}

public sealed record PreviewRetrievalResponse
{
    public required IReadOnlyList<PreviewRetrievalChunk> Chunks { get; init; }
    public required int TotalChunksForConnector { get; init; }
    public required bool HasEvidence { get; init; }
    public string? Message { get; init; }
}

public sealed record PreviewRetrievalChunk
{
    public required string ChunkId { get; init; }
    public required string Title { get; init; }
    public required string ChunkText { get; init; }
    public required string SourceType { get; init; }
    public string? ProductArea { get; init; }
    public required double Score { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}

public sealed record MissingFieldAnalysis
{
    public required IReadOnlyList<string> MissingRequiredFields { get; init; }
    public required IReadOnlyList<FieldCoverage> FieldCoverage { get; init; }
}

public sealed record FieldCoverage
{
    public required string FieldName { get; init; }
    public required bool IsMapped { get; init; }
    public required bool IsRequired { get; init; }
}

public sealed record ConnectorValidationResult
{
    public required bool IsValid { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
    public MissingFieldAnalysis? MissingFieldAnalysis { get; init; }

    public static ConnectorValidationResult Valid(MissingFieldAnalysis? analysis = null) =>
        new() { IsValid = true, Errors = [], MissingFieldAnalysis = analysis };

    public static ConnectorValidationResult Invalid(params string[] errors) =>
        new() { IsValid = false, Errors = errors };

    public static ConnectorValidationResult Invalid(IReadOnlyList<string> errors, MissingFieldAnalysis? analysis = null) =>
        new() { IsValid = false, Errors = errors, MissingFieldAnalysis = analysis };
}
