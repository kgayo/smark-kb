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
    public const string ConnectorOAuthCompleted = "connector.oauth_completed";
    public const string ConnectorOAuthFailed = "connector.oauth_failed";

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

    // Routing improvement events (P1-009).
    public const string RoutingRuleCreated = "routing.rule.created";
    public const string RoutingRuleUpdated = "routing.rule.updated";
    public const string RoutingRuleDeleted = "routing.rule.deleted";
    public const string RoutingRecommendationGenerated = "routing.recommendation.generated";
    public const string RoutingRecommendationApplied = "routing.recommendation.applied";
    public const string RoutingRecommendationDismissed = "routing.recommendation.dismissed";

    // Privacy and compliance events (P2-001).
    public const string PiiPolicyUpdated = "privacy.pii_policy_updated";
    public const string PiiRedactionAudit = "privacy.pii_redaction_audit";
    public const string RetentionPolicyUpdated = "privacy.retention_policy_updated";
    public const string RetentionCleanupExecuted = "privacy.retention_cleanup_executed";
    public const string RetentionComplianceChecked = "privacy.retention_compliance_checked";
    public const string DataSubjectDeletionRequested = "privacy.deletion_requested";
    public const string DataSubjectDeletionCompleted = "privacy.deletion_completed";
    public const string DataSubjectDeletionFailed = "privacy.deletion_failed";

    // Team playbook events (P2-002).
    public const string PlaybookCreated = "playbook.created";
    public const string PlaybookUpdated = "playbook.updated";
    public const string PlaybookDeleted = "playbook.deleted";
    public const string PlaybookValidationFailed = "playbook.validation_failed";

    // Pattern maintenance events (P2-004).
    public const string ContradictionDetected = "pattern.contradiction.detected";
    public const string ContradictionDetectionRun = "pattern.contradiction.detection_run";
    public const string ContradictionResolved = "pattern.contradiction.resolved";
    public const string MaintenanceDetectionRun = "pattern.maintenance.detection_run";
    public const string MaintenanceTaskCreated = "pattern.maintenance.task_created";
    public const string MaintenanceTaskResolved = "pattern.maintenance.task_resolved";
    public const string MaintenanceTaskDismissed = "pattern.maintenance.task_dismissed";

    // Scheduled sync events (P3-018).
    public const string ScheduledSyncTriggered = "scheduled_sync.triggered";
    public const string ScheduledSyncSkipped = "scheduled_sync.skipped";

    // Eval report events (P3-021).
    public const string EvalReportPersisted = "eval.report_persisted";

    // Eval notification events (P3-007).
    public const string EvalNotificationSent = "eval.notification_sent";
    public const string EvalNotificationFailed = "eval.notification_failed";

    // Webhook events.
    public const string WebhookReceived = "webhook.received";
    public const string WebhookSignatureFailed = "webhook.signature_failed";
    public const string WebhookClientStateMismatch = "webhook.clientstate_mismatch";
    public const string WebhookPollFallback = "webhook.poll_fallback";
}
