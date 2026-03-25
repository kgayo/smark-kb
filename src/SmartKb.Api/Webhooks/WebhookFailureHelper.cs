using Microsoft.EntityFrameworkCore;
using SmartKb.Contracts.Configuration;
using SmartKb.Data;
using SmartKb.Data.Entities;

namespace SmartKb.Api.Webhooks;

/// <summary>
/// Shared helper for recording webhook delivery failures and computing polling fallback times.
/// Consolidates identical logic previously duplicated across all 4 webhook handlers.
/// </summary>
public static class WebhookFailureHelper
{
    /// <summary>
    /// Records a webhook delivery failure and activates polling fallback if the failure threshold is exceeded.
    /// </summary>
    public static async Task RecordDeliveryFailureAsync(
        SmartKbDbContext db,
        WebhookSettings webhookSettings,
        Guid connectorId,
        string connectorType,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var subscriptions = await db.Set<WebhookSubscriptionEntity>()
            .Where(w => w.ConnectorId == connectorId && w.IsActive)
            .ToListAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        foreach (var sub in subscriptions)
        {
            sub.ConsecutiveFailures++;
            sub.UpdatedAt = now;

            if (sub.ConsecutiveFailures >= webhookSettings.FailureThresholdForFallback)
            {
                sub.PollingFallbackActive = true;
                sub.NextPollAt = ComputeNextPollTime(webhookSettings, now);
                logger.LogWarning(
                    "{ConnectorType} webhook fallback activated: connector={ConnectorId}, failures={Failures}, nextPoll={NextPoll}",
                    connectorType, connectorId, sub.ConsecutiveFailures, sub.NextPollAt);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Computes the next polling time by adding the configured interval plus random jitter.
    /// </summary>
    public static DateTimeOffset ComputeNextPollTime(WebhookSettings webhookSettings, DateTimeOffset from)
    {
        var intervalSeconds = webhookSettings.PollingFallbackIntervalSeconds;
        var jitter = Random.Shared.Next(0, webhookSettings.PollingJitterMaxSeconds + 1);
        return from.AddSeconds(intervalSeconds + jitter);
    }
}
