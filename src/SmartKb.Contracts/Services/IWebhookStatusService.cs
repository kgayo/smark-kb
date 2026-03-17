using SmartKb.Contracts.Models;

namespace SmartKb.Contracts.Services;

/// <summary>
/// Service for querying webhook subscription health status (P1-008).
/// </summary>
public interface IWebhookStatusService
{
    /// <summary>
    /// Gets webhook subscription statuses for a specific connector.
    /// </summary>
    Task<WebhookStatusListResponse> GetByConnectorAsync(
        string tenantId, Guid connectorId, CancellationToken ct = default);

    /// <summary>
    /// Gets all webhook subscription statuses for a tenant.
    /// </summary>
    Task<WebhookStatusListResponse> GetAllAsync(
        string tenantId, CancellationToken ct = default);

    /// <summary>
    /// Gets aggregated diagnostics summary for a tenant.
    /// </summary>
    Task<DiagnosticsSummaryResponse> GetDiagnosticsSummaryAsync(
        string tenantId, CancellationToken ct = default);
}
