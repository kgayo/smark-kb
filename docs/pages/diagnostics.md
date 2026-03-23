# Diagnostics Page

**Route:** `/diagnostics`
**Required Role:** Admin
**Component:** `DiagnosticsPage.tsx`

Monitor system health, webhook status, dead-letter queue, and SLO targets. Three tabs organize the diagnostic information.

## Tabs

### Overview Tab

**Service Status Cards**: shows availability of core services:
- Service Bus
- Key Vault
- OpenAI
- Search Service

**SLO Targets Table**:

| Metric | Target |
|--------|--------|
| Answer Latency P95 | < 8000ms |
| Availability | > 99.5% |
| Sync Lag P95 | < configured minutes |
| No-Evidence Rate | < configured threshold |
| Dead-Letter Depth | < configured threshold |
| Rate-Limit Alert | configured hits / configured window (minutes) |

**Secrets Status**: Key Vault connectivity, OpenAI configuration, active model name.

**Credentials Card**: shows credential health across all connectors — counts of expired, critical (expiring within 7 days), and warning (expiring within 30 days) credentials. Displays "All healthy" when no issues detected.

**Rate Limits Card**: shows the number of connectors currently being throttled (HTTP 429). When no connectors are rate-limited, displays "No rate-limit alerts."

**Connector Health Table**: per-connector metrics including status, last sync time/status, webhook count, fallback count, failure count, and rate-limit hits (with warning badge when alerting).

### Webhooks Tab

**Summary**: total webhooks, active count, fallback count.

**Subscriptions Table**:

| Column | Description |
|--------|-------------|
| Connector | Connector name and type |
| Event Type | Webhook event being monitored |
| Status | Healthy, Fallback, Inactive, or Degraded |
| Consecutive Failures | Count of sequential failures |
| Last Delivery | Timestamp of last successful delivery |
| Next Poll | When the next polling fallback will fire |

### Dead Letters Tab

**Dead-Letter Queue Viewer**: inspect failed ingestion messages.

| Column | Description |
|--------|-------------|
| Message ID | Truncated Service Bus message ID |
| Subject | Message subject/topic |
| Reason | Why the message was dead-lettered |
| Delivery Count | Number of delivery attempts |
| Enqueued | When the message entered the queue |

Click a row to expand and see:
- Correlation ID
- Error description
- Full message body (JSON formatted)

The tab badge shows the count of dead-letter messages as a visual alert.

## API Endpoints Used

- `GET /api/admin/diagnostics/summary` — full diagnostics summary
- `GET /api/admin/slo/status` — SLO targets and metrics
- `GET /api/admin/secrets/status` — secrets configuration status
- `GET /api/admin/webhooks` — all webhook statuses
- `GET /api/admin/ingestion/dead-letters` — dead-letter queue messages
- `GET /api/admin/diagnostics/rate-limit-alerts` — per-connector rate-limit alerts
