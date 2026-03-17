namespace SmartKb.Contracts;

/// <summary>
/// Canonical audit event type constants. All audit event writes must use these
/// instead of string literals to prevent typos and enable centralized discovery.
/// </summary>
public static class AuditEventTypes
{
    // Tenant / auth events.
    public const string TenantMissing = "tenant.missing";

    // Chat events.
    public const string ChatFeedback = "chat.feedback";
    public const string ChatOutcome = "chat.outcome";

    // PII redaction.
    public const string PiiRedaction = "pii.redaction";

    // Escalation events.
    public const string EscalationDraftCreated = "escalation.draft.created";
    public const string EscalationDraftApproved = "escalation.draft.approved";
    public const string EscalationExternalCreated = "escalation.external.created";
    public const string EscalationExternalFailed = "escalation.external.failed";

    // Connector admin events.
    public const string ConnectorCreated = "connector.created";
    public const string ConnectorUpdated = "connector.updated";
    public const string ConnectorDeleted = "connector.deleted";
    public const string ConnectorEnabled = "connector.enabled";
    public const string ConnectorDisabled = "connector.disabled";
    public const string ConnectorTestPassed = "connector.test_passed";
    public const string ConnectorTestFailed = "connector.test_failed";
    public const string ConnectorSyncTriggered = "connector.sync_triggered";
    public const string ConnectorPreview = "connector.preview";

    // Sync (ingestion) events.
    public const string SyncCompleted = "sync.completed";
    public const string SyncFailed = "sync.failed";

    // Pattern distillation events.
    public const string PatternDistilled = "pattern.distilled";
    public const string PatternDistillationRun = "pattern.distillation_run";

    // Pattern governance events (P1-006).
    public const string PatternReviewed = "pattern.reviewed";
    public const string PatternApproved = "pattern.approved";
    public const string PatternDeprecated = "pattern.deprecated";

    // Webhook events.
    public const string WebhookReceived = "webhook.received";
    public const string WebhookSignatureFailed = "webhook.signature_failed";
    public const string WebhookClientStateMismatch = "webhook.clientstate_mismatch";
    public const string WebhookPollFallback = "webhook.poll_fallback";
}
