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

    /// <summary>Chat orchestration duration in milliseconds (histogram for P50/P95/P99).</summary>
    public static readonly Histogram<double> ChatLatencyMs =
        Meter.CreateHistogram<double>(
            "smartkb.chat.latency_ms",
            unit: "ms",
            description: "Chat orchestration answer-ready latency in milliseconds.");

    /// <summary>Total chat requests processed (counter).</summary>
    public static readonly Counter<long> ChatRequestsTotal =
        Meter.CreateCounter<long>(
            "smartkb.chat.requests_total",
            description: "Total chat requests processed.");

    /// <summary>Chat requests that returned no evidence (counter).</summary>
    public static readonly Counter<long> ChatNoEvidenceTotal =
        Meter.CreateCounter<long>(
            "smartkb.chat.no_evidence_total",
            description: "Chat requests where no evidence was found.");

    /// <summary>Sync job processing duration in milliseconds (histogram).</summary>
    public static readonly Histogram<double> SyncJobDurationMs =
        Meter.CreateHistogram<double>(
            "smartkb.ingestion.sync_duration_ms",
            unit: "ms",
            description: "Sync job processing duration in milliseconds.");

    /// <summary>Sync jobs completed successfully (counter).</summary>
    public static readonly Counter<long> SyncJobsCompletedTotal =
        Meter.CreateCounter<long>(
            "smartkb.ingestion.sync_completed_total",
            description: "Sync jobs completed successfully.");

    /// <summary>Sync jobs that failed (counter).</summary>
    public static readonly Counter<long> SyncJobsFailedTotal =
        Meter.CreateCounter<long>(
            "smartkb.ingestion.sync_failed_total",
            description: "Sync jobs that failed.");

    /// <summary>Messages dead-lettered (counter).</summary>
    public static readonly Counter<long> DeadLetterTotal =
        Meter.CreateCounter<long>(
            "smartkb.ingestion.dead_letter_total",
            description: "Messages sent to the dead-letter queue.");

    /// <summary>Records processed by ingestion (counter).</summary>
    public static readonly Counter<long> RecordsProcessedTotal =
        Meter.CreateCounter<long>(
            "smartkb.ingestion.records_processed_total",
            description: "Total records processed by ingestion.");

    /// <summary>PII redaction events (counter).</summary>
    public static readonly Counter<long> PiiRedactionsTotal =
        Meter.CreateCounter<long>(
            "smartkb.security.pii_redactions_total",
            description: "Chunks where PII was redacted before model context.");

    /// <summary>Restricted content blocked at orchestration layer (counter).</summary>
    public static readonly Counter<long> RestrictedContentBlockedTotal =
        Meter.CreateCounter<long>(
            "smartkb.security.restricted_content_blocked_total",
            description: "Restricted content chunks blocked at orchestration defense-in-depth layer.");

    /// <summary>Chat response confidence (histogram for distribution analysis).</summary>
    public static readonly Histogram<double> ChatConfidence =
        Meter.CreateHistogram<double>(
            "smartkb.chat.confidence",
            description: "Blended confidence score distribution for chat responses.");

    /// <summary>Retention cleanup records deleted (counter).</summary>
    public static readonly Counter<long> RetentionCleanupDeletedTotal =
        Meter.CreateCounter<long>(
            "smartkb.privacy.retention_cleanup_deleted_total",
            description: "Total records deleted by retention cleanup.");

    /// <summary>Data subject deletion requests processed (counter).</summary>
    public static readonly Counter<long> DataSubjectDeletionsTotal =
        Meter.CreateCounter<long>(
            "smartkb.privacy.data_subject_deletions_total",
            description: "Total data subject deletion requests processed.");

    /// <summary>Retention cleanup execution duration in milliseconds (histogram).</summary>
    public static readonly Histogram<long> RetentionCleanupDurationMs =
        Meter.CreateHistogram<long>(
            "smartkb.privacy.retention_cleanup_duration_ms",
            unit: "ms",
            description: "Duration of retention cleanup execution per entity type.");

    /// <summary>Retention compliance checks performed (counter).</summary>
    public static readonly Counter<long> RetentionComplianceChecksTotal =
        Meter.CreateCounter<long>(
            "smartkb.privacy.retention_compliance_checks_total",
            description: "Total retention compliance checks performed.");

    /// <summary>Overdue retention policies detected (counter).</summary>
    public static readonly Counter<long> RetentionOverduePoliciesTotal =
        Meter.CreateCounter<long>(
            "smartkb.privacy.retention_overdue_policies_total",
            description: "Retention policies detected as overdue during compliance checks.");

    /// <summary>Source API rate-limit hits (HTTP 429) during connector sync (counter).</summary>
    public static readonly Counter<long> SourceRateLimitTotal =
        Meter.CreateCounter<long>(
            "smartkb.ingestion.source_rate_limit_total",
            description: "Source API rate-limit (429) responses encountered during ingestion.");

    // --- Cost Optimization Metrics (P2-003) ---

    /// <summary>Prompt tokens consumed per request (histogram).</summary>
    public static readonly Histogram<long> PromptTokensUsed =
        Meter.CreateHistogram<long>(
            "smartkb.cost.prompt_tokens",
            unit: "tokens",
            description: "Prompt tokens consumed per chat request.");

    /// <summary>Completion tokens consumed per request (histogram).</summary>
    public static readonly Histogram<long> CompletionTokensUsed =
        Meter.CreateHistogram<long>(
            "smartkb.cost.completion_tokens",
            unit: "tokens",
            description: "Completion tokens consumed per chat request.");

    /// <summary>Embedding cache hits (counter).</summary>
    public static readonly Counter<long> EmbeddingCacheHitsTotal =
        Meter.CreateCounter<long>(
            "smartkb.cost.embedding_cache_hits_total",
            description: "Embedding cache hit count.");

    /// <summary>Embedding cache misses (counter).</summary>
    public static readonly Counter<long> EmbeddingCacheMissesTotal =
        Meter.CreateCounter<long>(
            "smartkb.cost.embedding_cache_misses_total",
            description: "Embedding cache miss count.");

    /// <summary>Estimated cost per request in USD (histogram).</summary>
    public static readonly Histogram<double> EstimatedCostUsd =
        Meter.CreateHistogram<double>(
            "smartkb.cost.estimated_cost_usd",
            unit: "USD",
            description: "Estimated cost per chat request in USD.");

    /// <summary>Requests denied due to budget limits (counter).</summary>
    public static readonly Counter<long> BudgetDeniedTotal =
        Meter.CreateCounter<long>(
            "smartkb.cost.budget_denied_total",
            description: "Chat requests denied due to token budget exhaustion.");

    /// <summary>Evidence chunks truncated by retrieval compression (counter).</summary>
    public static readonly Counter<long> RetrievalCompressionTruncatedTotal =
        Meter.CreateCounter<long>(
            "smartkb.cost.retrieval_compression_truncated_total",
            description: "Evidence chunks truncated by retrieval compression.");
}
