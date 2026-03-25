namespace SmartKb.Contracts.Models;

/// <summary>
/// Shared response message constants used across API endpoints and webhook handlers
/// to eliminate hardcoded string duplication.
/// </summary>
public static class ResponseMessages
{
    public const string ConnectorNotFound = "Connector not found.";
    public const string SessionNotFound = "Session not found.";
    public const string SearchServiceNotConfigured = "Search service is not configured.";
    public const string ConnectorDisabledEventIgnored = "Connector is disabled; event ignored.";
    public const string NoActiveWebhookSubscriptions = "No active webhook subscriptions.";
    public const string InvalidWebhookPayload = "Invalid webhook payload.";
    public const string InvalidWebhookSignature = "Invalid webhook signature.";
    public const string FailedToVerifyWebhookSignature = "Failed to verify webhook signature.";

    /// <summary>
    /// Actor ID used for system-initiated audit events (webhooks, background services).
    /// </summary>
    public const string SystemActorId = "system";
}
