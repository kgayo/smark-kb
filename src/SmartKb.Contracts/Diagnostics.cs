using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace SmartKb.Contracts;

/// <summary>
/// Named ActivitySources for distributed tracing across SmartKb services.
/// Register these sources with the OpenTelemetry SDK so custom spans are exported.
/// </summary>
public static class Diagnostics
{
    public const string ApiSourceName = "SmartKb.Api";
    public const string OrchestrationSourceName = "SmartKb.Orchestration";
    public const string IngestionSourceName = "SmartKb.Ingestion";

    public static readonly ActivitySource ApiSource = new(ApiSourceName);
    public static readonly ActivitySource OrchestrationSource = new(OrchestrationSourceName);
    public static readonly ActivitySource IngestionSource = new(IngestionSourceName);

    // --- OTel Meters for SLO dashboards and alerts (P0-022) ---

    public const string MeterName = "SmartKb";
    public static readonly Meter Meter = new(MeterName, "1.0.0");

    /// <summary>
    /// Canonical metric name constants for all OTel instruments.
    /// Reference these instead of hardcoding metric name strings.
    /// </summary>
    public static class MetricNames
    {
        // Chat metrics
        public const string ChatLatencyMs = "smartkb.chat.latency_ms";
        public const string ChatRequestsTotal = "smartkb.chat.requests_total";
        public const string ChatNoEvidenceTotal = "smartkb.chat.no_evidence_total";
        public const string ChatConfidence = "smartkb.chat.confidence";
        public const string ChatSessionSummarizationsTotal = "smartkb.chat.session_summarizations_total";

        // Ingestion metrics
        public const string SyncDurationMs = "smartkb.ingestion.sync_duration_ms";
        public const string SyncCompletedTotal = "smartkb.ingestion.sync_completed_total";
        public const string SyncFailedTotal = "smartkb.ingestion.sync_failed_total";
        public const string DeadLetterTotal = "smartkb.ingestion.dead_letter_total";
        public const string RecordsProcessedTotal = "smartkb.ingestion.records_processed_total";
        public const string ScheduledSyncTriggeredTotal = "smartkb.ingestion.scheduled_sync_triggered_total";
        public const string SourceRateLimitTotal = "smartkb.ingestion.source_rate_limit_total";

        // Security metrics
        public const string PiiRedactionsTotal = "smartkb.security.pii_redactions_total";
        public const string RestrictedContentBlockedTotal = "smartkb.security.restricted_content_blocked_total";
        public const string CredentialRotationsTotal = "smartkb.security.credential_rotations_total";
        public const string CredentialExpiryWarningsTotal = "smartkb.security.credential_expiry_warnings_total";

        // Privacy metrics
        public const string RetentionCleanupDeletedTotal = "smartkb.privacy.retention_cleanup_deleted_total";
        public const string DataSubjectDeletionsTotal = "smartkb.privacy.data_subject_deletions_total";
        public const string RetentionCleanupDurationMs = "smartkb.privacy.retention_cleanup_duration_ms";
        public const string RetentionComplianceChecksTotal = "smartkb.privacy.retention_compliance_checks_total";
        public const string RetentionOverduePoliciesTotal = "smartkb.privacy.retention_overdue_policies_total";

        // Cost metrics
        public const string PromptTokens = "smartkb.cost.prompt_tokens";
        public const string CompletionTokens = "smartkb.cost.completion_tokens";
        public const string EmbeddingCacheHitsTotal = "smartkb.cost.embedding_cache_hits_total";
        public const string EmbeddingCacheMissesTotal = "smartkb.cost.embedding_cache_misses_total";
        public const string EmbeddingCacheEvictionsTotal = "smartkb.cost.embedding_cache_evictions_total";
        public const string EstimatedCostUsd = "smartkb.cost.estimated_cost_usd";
        public const string BudgetDeniedTotal = "smartkb.cost.budget_denied_total";
        public const string RetrievalCompressionTruncatedTotal = "smartkb.cost.retrieval_compression_truncated_total";

        // Eval metrics
        public const string EvalNotificationsSentTotal = "smartkb.eval.notifications_sent_total";
        public const string EvalNotificationFailuresTotal = "smartkb.eval.notification_failures_total";
    }

    /// <summary>Chat orchestration duration in milliseconds (histogram for P50/P95/P99).</summary>
    public static readonly Histogram<double> ChatLatencyMs =
        Meter.CreateHistogram<double>(
            MetricNames.ChatLatencyMs,
            unit: "ms",
            description: "Chat orchestration answer-ready latency in milliseconds.");

    /// <summary>Total chat requests processed (counter).</summary>
    public static readonly Counter<long> ChatRequestsTotal =
        Meter.CreateCounter<long>(
            MetricNames.ChatRequestsTotal,
            description: "Total chat requests processed.");

    /// <summary>Chat requests that returned no evidence (counter).</summary>
    public static readonly Counter<long> ChatNoEvidenceTotal =
        Meter.CreateCounter<long>(
            MetricNames.ChatNoEvidenceTotal,
            description: "Chat requests where no evidence was found.");

    /// <summary>Sync job processing duration in milliseconds (histogram).</summary>
    public static readonly Histogram<double> SyncJobDurationMs =
        Meter.CreateHistogram<double>(
            MetricNames.SyncDurationMs,
            unit: "ms",
            description: "Sync job processing duration in milliseconds.");

    /// <summary>Sync jobs completed successfully (counter).</summary>
    public static readonly Counter<long> SyncJobsCompletedTotal =
        Meter.CreateCounter<long>(
            MetricNames.SyncCompletedTotal,
            description: "Sync jobs completed successfully.");

    /// <summary>Sync jobs that failed (counter).</summary>
    public static readonly Counter<long> SyncJobsFailedTotal =
        Meter.CreateCounter<long>(
            MetricNames.SyncFailedTotal,
            description: "Sync jobs that failed.");

    /// <summary>Messages dead-lettered (counter).</summary>
    public static readonly Counter<long> DeadLetterTotal =
        Meter.CreateCounter<long>(
            MetricNames.DeadLetterTotal,
            description: "Messages sent to the dead-letter queue.");

    /// <summary>Records processed by ingestion (counter).</summary>
    public static readonly Counter<long> RecordsProcessedTotal =
        Meter.CreateCounter<long>(
            MetricNames.RecordsProcessedTotal,
            description: "Total records processed by ingestion.");

    /// <summary>PII redaction events (counter).</summary>
    public static readonly Counter<long> PiiRedactionsTotal =
        Meter.CreateCounter<long>(
            MetricNames.PiiRedactionsTotal,
            description: "Chunks where PII was redacted before model context.");

    /// <summary>Restricted content blocked at orchestration layer (counter).</summary>
    public static readonly Counter<long> RestrictedContentBlockedTotal =
        Meter.CreateCounter<long>(
            MetricNames.RestrictedContentBlockedTotal,
            description: "Restricted content chunks blocked at orchestration defense-in-depth layer.");

    /// <summary>Chat response confidence (histogram for distribution analysis).</summary>
    public static readonly Histogram<double> ChatConfidence =
        Meter.CreateHistogram<double>(
            MetricNames.ChatConfidence,
            description: "Blended confidence score distribution for chat responses.");

    /// <summary>Retention cleanup records deleted (counter).</summary>
    public static readonly Counter<long> RetentionCleanupDeletedTotal =
        Meter.CreateCounter<long>(
            MetricNames.RetentionCleanupDeletedTotal,
            description: "Total records deleted by retention cleanup.");

    /// <summary>Data subject deletion requests processed (counter).</summary>
    public static readonly Counter<long> DataSubjectDeletionsTotal =
        Meter.CreateCounter<long>(
            MetricNames.DataSubjectDeletionsTotal,
            description: "Total data subject deletion requests processed.");

    /// <summary>Retention cleanup execution duration in milliseconds (histogram).</summary>
    public static readonly Histogram<long> RetentionCleanupDurationMs =
        Meter.CreateHistogram<long>(
            MetricNames.RetentionCleanupDurationMs,
            unit: "ms",
            description: "Duration of retention cleanup execution per entity type.");

    /// <summary>Retention compliance checks performed (counter).</summary>
    public static readonly Counter<long> RetentionComplianceChecksTotal =
        Meter.CreateCounter<long>(
            MetricNames.RetentionComplianceChecksTotal,
            description: "Total retention compliance checks performed.");

    /// <summary>Overdue retention policies detected (counter).</summary>
    public static readonly Counter<long> RetentionOverduePoliciesTotal =
        Meter.CreateCounter<long>(
            MetricNames.RetentionOverduePoliciesTotal,
            description: "Retention policies detected as overdue during compliance checks.");

    /// <summary>Scheduled sync jobs triggered (counter).</summary>
    public static readonly Counter<long> ScheduledSyncTriggeredTotal =
        Meter.CreateCounter<long>(
            MetricNames.ScheduledSyncTriggeredTotal,
            description: "Scheduled sync jobs triggered by cron evaluation.");

    /// <summary>Source API rate-limit hits (HTTP 429) during connector sync (counter).</summary>
    public static readonly Counter<long> SourceRateLimitTotal =
        Meter.CreateCounter<long>(
            MetricNames.SourceRateLimitTotal,
            description: "Source API rate-limit (429) responses encountered during ingestion.");

    // --- Cost Optimization Metrics (P2-003) ---

    /// <summary>Prompt tokens consumed per request (histogram).</summary>
    public static readonly Histogram<long> PromptTokensUsed =
        Meter.CreateHistogram<long>(
            MetricNames.PromptTokens,
            unit: "tokens",
            description: "Prompt tokens consumed per chat request.");

    /// <summary>Completion tokens consumed per request (histogram).</summary>
    public static readonly Histogram<long> CompletionTokensUsed =
        Meter.CreateHistogram<long>(
            MetricNames.CompletionTokens,
            unit: "tokens",
            description: "Completion tokens consumed per chat request.");

    /// <summary>Embedding cache hits (counter).</summary>
    public static readonly Counter<long> EmbeddingCacheHitsTotal =
        Meter.CreateCounter<long>(
            MetricNames.EmbeddingCacheHitsTotal,
            description: "Embedding cache hit count.");

    /// <summary>Embedding cache misses (counter).</summary>
    public static readonly Counter<long> EmbeddingCacheMissesTotal =
        Meter.CreateCounter<long>(
            MetricNames.EmbeddingCacheMissesTotal,
            description: "Embedding cache miss count.");

    /// <summary>Embedding cache entries evicted by background cleanup (counter).</summary>
    public static readonly Counter<long> EmbeddingCacheEvictionsTotal =
        Meter.CreateCounter<long>(
            MetricNames.EmbeddingCacheEvictionsTotal,
            description: "Expired embedding cache entries evicted by background worker.");

    /// <summary>Estimated cost per request in USD (histogram).</summary>
    public static readonly Histogram<double> EstimatedCostUsd =
        Meter.CreateHistogram<double>(
            MetricNames.EstimatedCostUsd,
            unit: "USD",
            description: "Estimated cost per chat request in USD.");

    /// <summary>Requests denied due to budget limits (counter).</summary>
    public static readonly Counter<long> BudgetDeniedTotal =
        Meter.CreateCounter<long>(
            MetricNames.BudgetDeniedTotal,
            description: "Chat requests denied due to token budget exhaustion.");

    /// <summary>Evidence chunks truncated by retrieval compression (counter).</summary>
    public static readonly Counter<long> RetrievalCompressionTruncatedTotal =
        Meter.CreateCounter<long>(
            MetricNames.RetrievalCompressionTruncatedTotal,
            description: "Evidence chunks truncated by retrieval compression.");

    // --- Eval Notification Metrics (P3-007) ---

    /// <summary>Eval regression notifications sent successfully (counter).</summary>
    public static readonly Counter<long> EvalNotificationsSentTotal =
        Meter.CreateCounter<long>(
            MetricNames.EvalNotificationsSentTotal,
            description: "Eval regression alert notifications sent successfully.");

    /// <summary>Eval regression notifications that failed to send (counter).</summary>
    public static readonly Counter<long> EvalNotificationFailuresTotal =
        Meter.CreateCounter<long>(
            MetricNames.EvalNotificationFailuresTotal,
            description: "Eval regression alert notifications that failed to send.");

    // --- Credential Rotation Metrics (P3-009) ---

    /// <summary>Successful credential rotations (counter).</summary>
    public static readonly Counter<long> CredentialRotationsTotal =
        Meter.CreateCounter<long>(
            MetricNames.CredentialRotationsTotal,
            description: "Successful credential rotation operations.");

    /// <summary>Credential expiry warnings surfaced (counter).</summary>
    public static readonly Counter<long> CredentialExpiryWarningsTotal =
        Meter.CreateCounter<long>(
            MetricNames.CredentialExpiryWarningsTotal,
            description: "Credential expiry warnings surfaced during status checks.");

    // --- Session Summarization Metrics (P3-002) ---

    /// <summary>Session summarizations performed (counter).</summary>
    public static readonly Counter<long> SessionSummarizationsTotal =
        Meter.CreateCounter<long>(
            MetricNames.ChatSessionSummarizationsTotal,
            description: "Session context summarizations performed when sliding window drops messages.");

    /// <summary>
    /// Shared OTel span tag name constants used by ChatOrchestrator, SyncJobProcessor, IngestionWorker,
    /// and ScheduledSyncService. Eliminates hardcoded string literals for compile-time safety.
    /// </summary>
    public static class TagNames
    {
        // Common tags
        public const string TenantId = "smartkb.tenant_id";
        public const string UserId = "smartkb.user_id";
        public const string CorrelationId = "smartkb.correlation_id";
        public const string Reason = "smartkb.reason";

        // Orchestration tags
        public const string ResponseType = "smartkb.response_type";
        public const string Model = "smartkb.model";
        public const string PromptTokens = "smartkb.prompt_tokens";
        public const string CompletionTokens = "smartkb.completion_tokens";
        public const string BlendedConfidence = "smartkb.blended_confidence";
        public const string CitationCount = "smartkb.citation_count";
        public const string DurationMs = "smartkb.duration_ms";

        // Embedding tags
        public const string EmbeddingCacheHit = "smartkb.embedding_cache_hit";
        public const string EmbeddingDims = "smartkb.embedding_dims";

        // Retrieval tags
        public const string ChunkCount = "smartkb.chunk_count";
        public const string HasEvidence = "smartkb.has_evidence";
        public const string AclFiltered = "smartkb.acl_filtered";

        // Classification tags
        public const string ClassificationCategory = "smartkb.classification.category";
        public const string ClassificationProductArea = "smartkb.classification.product_area";
        public const string ClassificationSeverity = "smartkb.classification.severity";
        public const string ClassificationConfidence = "smartkb.classification.confidence";

        // Summarization tags
        public const string SummarizationDroppedCount = "smartkb.summarization.dropped_count";
        public const string SummarizationSummaryLength = "smartkb.summarization.summary_length";

        // Ingestion tags
        public const string SyncRunId = "smartkb.sync_run_id";
        public const string ConnectorId = "smartkb.connector_id";
        public const string ConnectorType = "smartkb.connector_type";
        public const string IsBackfill = "smartkb.is_backfill";
        public const string RecordsProcessed = "smartkb.records_processed";
        public const string RecordsFailed = "smartkb.records_failed";
        public const string ChunksProduced = "smartkb.chunks_produced";

        // Scheduled sync tags
        public const string ScheduledSyncConnectorsEvaluated = "smartkb.scheduled_sync.connectors_evaluated";
        public const string ScheduledSyncTriggeredCount = "smartkb.scheduled_sync.triggered_count";
    }
}
