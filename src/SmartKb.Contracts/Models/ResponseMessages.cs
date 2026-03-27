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

    // Connector client error messages (shared across all 4 connector clients).
    public const string InvalidOrMissingSourceConfiguration = "Invalid or missing source configuration.";
    public const string NoCredentialsProvided = "No credentials provided.";

    // Endpoint-specific "not found" messages (shared across endpoint files).
    public const string EscalationDraftNotFound = "Escalation draft not found.";
    public const string EvidenceChunkNotFound = "Evidence chunk not found.";
    public const string SynonymRuleNotFound = "Synonym rule not found.";
    public const string PlaybookNotFound = "Playbook not found.";
    public const string RoutingRuleNotFound = "Routing rule not found.";
    public const string GoldCaseNotFound = "Gold case not found.";
    public const string EvalReportNotFound = "Eval report not found.";
    public const string DeletionRequestNotFound = "Deletion request not found.";
    public const string SyncRunNotFound = "Sync run not found.";
    public const string FeedbackNotFound = "Feedback not found.";
    public const string StopWordNotFound = "Stop word not found.";
    public const string SpecialTokenNotFound = "Special token not found.";
    public const string PatternNotFound = "Pattern not found.";
    public const string ContradictionNotFound = "Contradiction not found.";
    public const string MaintenanceTaskNotFound = "Maintenance task not found.";
    public const string SessionNotFoundOrExpired = "Session not found or expired.";
    public const string PlaybookNotFoundForTeam = "Playbook not found for team.";
    public const string OAuthNotConfigured = "Connector not found, not configured for OAuth, or OAuth is not enabled.";
    public const string InvalidOrExpiredStateParameter = "Invalid or expired state parameter.";
    public const string NoTenantCostOverrides = "No tenant cost overrides found.";
    public const string RetiredVersionNotFound = "Retired version not found or not eligible for deletion.";
    public const string NoTenantOverrides = "No tenant overrides found.";
    public const string NoCustomPiiPolicy = "No custom PII policy found.";
    public const string RecommendationNotPending = "Recommendation not found or not pending.";

    /// <summary>
    /// Actor ID used for system-initiated audit events (webhooks, background services).
    /// </summary>
    public const string SystemActorId = "system";
}
