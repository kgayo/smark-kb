namespace SmartKb.Contracts.Services;

/// <summary>
/// Service for recording and evaluating rate-limit (HTTP 429) events per connector (P3-020).
/// </summary>
public interface IRateLimitAlertService
{
    /// <summary>Records a rate-limit event for a connector.</summary>
    Task RecordRateLimitEventAsync(
        string tenantId, Guid connectorId, string connectorType, CancellationToken ct = default);

    /// <summary>Gets active rate-limit alerts for a tenant based on threshold and window.</summary>
    Task<RateLimitAlertSummary> GetRateLimitAlertsAsync(
        string tenantId, CancellationToken ct = default);
}

/// <summary>
/// Summary of rate-limit alerts for a tenant.
/// </summary>
public sealed record RateLimitAlertSummary(
    int TotalAlertingConnectors,
    IReadOnlyList<ConnectorRateLimitAlert> Alerts);

/// <summary>
/// Rate-limit alert detail for a single connector.
/// </summary>
public sealed record ConnectorRateLimitAlert(
    Guid ConnectorId,
    string ConnectorName,
    string ConnectorType,
    int HitCount,
    DateTimeOffset? MostRecentHit,
    int Threshold,
    int WindowMinutes);
