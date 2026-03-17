using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;

namespace SmartKb.Data.Repositories;

public sealed class WebhookStatusService : IWebhookStatusService
{
    private readonly SmartKbDbContext _db;
    private readonly ILogger<WebhookStatusService> _logger;

    public WebhookStatusService(SmartKbDbContext db, ILogger<WebhookStatusService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<WebhookStatusListResponse> GetByConnectorAsync(
        string tenantId, Guid connectorId, CancellationToken ct = default)
    {
        var entities = await _db.WebhookSubscriptions
            .Include(w => w.Connector)
            .Where(w => w.TenantId == tenantId && w.ConnectorId == connectorId)
            .OrderBy(w => w.EventType)
            .ToListAsync(ct);

        return BuildResponse(entities.Select(MapToStatus).ToList());
    }

    public async Task<WebhookStatusListResponse> GetAllAsync(
        string tenantId, CancellationToken ct = default)
    {
        var entities = await _db.WebhookSubscriptions
            .Include(w => w.Connector)
            .Where(w => w.TenantId == tenantId)
            .OrderBy(w => w.Connector.Name)
            .ThenBy(w => w.EventType)
            .ToListAsync(ct);

        return BuildResponse(entities.Select(MapToStatus).ToList());
    }

    public async Task<DiagnosticsSummaryResponse> GetDiagnosticsSummaryAsync(
        string tenantId, CancellationToken ct = default)
    {
        var connectors = await _db.Connectors
            .Where(c => c.TenantId == tenantId)
            .Include(c => c.WebhookSubscriptions)
            .Include(c => c.SyncRuns)
            .ToListAsync(ct);

        var totalConnectors = connectors.Count;
        var enabled = connectors.Count(c => c.Status == Contracts.Enums.ConnectorStatus.Enabled);
        var disabled = totalConnectors - enabled;

        var allWebhooks = connectors.SelectMany(c => c.WebhookSubscriptions).ToList();
        var totalWebhooks = allWebhooks.Count;
        var activeWebhooks = allWebhooks.Count(w => w.IsActive && !w.PollingFallbackActive);
        var fallbackWebhooks = allWebhooks.Count(w => w.PollingFallbackActive);
        var failingWebhooks = allWebhooks.Count(w => w.ConsecutiveFailures > 0);

        var connectorHealth = connectors.Select(c =>
        {
            var lastSync = c.SyncRuns.OrderByDescending(s => s.StartedAt).FirstOrDefault();
            var webhooks = c.WebhookSubscriptions;
            return new ConnectorHealthSummary(
                c.Id,
                c.Name,
                c.ConnectorType.ToString(),
                c.Status.ToString(),
                lastSync?.Status.ToString(),
                lastSync?.CompletedAt ?? lastSync?.StartedAt,
                webhooks.Count,
                webhooks.Count(w => w.PollingFallbackActive),
                webhooks.Sum(w => w.ConsecutiveFailures));
        }).ToArray();

        return new DiagnosticsSummaryResponse(
            totalConnectors,
            enabled,
            disabled,
            totalWebhooks,
            activeWebhooks,
            fallbackWebhooks,
            failingWebhooks,
            ServiceBusConfigured: false, // Populated at API layer
            KeyVaultConfigured: false,   // Populated at API layer
            OpenAiConfigured: false,     // Populated at API layer
            SearchServiceConfigured: false, // Populated at API layer
            connectorHealth);
    }

    private static WebhookSubscriptionStatus MapToStatus(Data.Entities.WebhookSubscriptionEntity w) =>
        new(
            w.Id,
            w.ConnectorId,
            w.Connector.Name,
            w.Connector.ConnectorType.ToString(),
            w.EventType,
            w.IsActive,
            w.PollingFallbackActive,
            w.ConsecutiveFailures,
            w.LastDeliveryAt,
            w.NextPollAt,
            w.ExternalSubscriptionId,
            w.CreatedAt,
            w.UpdatedAt);

    private static WebhookStatusListResponse BuildResponse(
        List<WebhookSubscriptionStatus> subscriptions) =>
        new(
            subscriptions,
            subscriptions.Count,
            subscriptions.Count(s => s.IsActive && !s.PollingFallbackActive),
            subscriptions.Count(s => s.PollingFallbackActive));
}
