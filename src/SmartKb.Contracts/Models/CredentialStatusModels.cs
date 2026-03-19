namespace SmartKb.Contracts.Models;

/// <summary>
/// Health status of a connector's credential.
/// </summary>
public enum CredentialHealth
{
    Healthy,
    Warning,
    Critical,
    Expired,
    Missing,
    Unknown
}

/// <summary>
/// Credential status for a single connector.
/// </summary>
public sealed record ConnectorCredentialStatus(
    Guid ConnectorId,
    string ConnectorName,
    string ConnectorType,
    string AuthType,
    CredentialHealth Health,
    string? SecretName,
    DateTimeOffset? CreatedOn,
    DateTimeOffset? ExpiresOn,
    int? DaysUntilExpiry,
    int? AgeDays,
    string? Message);

/// <summary>
/// Aggregate credential status across all connectors for a tenant.
/// </summary>
public sealed record CredentialStatusSummary(
    IReadOnlyList<ConnectorCredentialStatus> Connectors,
    int TotalConnectors,
    int HealthyCount,
    int WarningCount,
    int CriticalCount,
    int ExpiredCount,
    int MissingCount);

/// <summary>
/// Result of a manual credential rotation attempt.
/// </summary>
public sealed record CredentialRotationResult(
    bool Success,
    string Message,
    DateTimeOffset? NewSecretCreatedOn);
