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

    /// <summary>Source API rate-limit hits (HTTP 429) during connector sync (counter).</summary>
    public static readonly Counter<long> SourceRateLimitTotal =
        Meter.CreateCounter<long>(
            "smartkb.ingestion.source_rate_limit_total",
            description: "Source API rate-limit (429) responses encountered during ingestion.");
}
