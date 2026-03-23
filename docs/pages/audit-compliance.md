# Audit & Compliance Page

**Route:** `/audit`
**Required Role:** Admin
**Component:** `AuditCompliancePage.tsx`

Self-serve audit event browsing and NDJSON export for compliance investigations. Two tabs organize audit functionality.

## Tabs

### Events Tab

Paginated table of audit events with filtering and expandable detail rows.

**Filters** (toolbar):
- **Event Type** — text filter (e.g., `connector.created`, `chat.feedback`)
- **Actor ID** — filter by the user or service that triggered the event
- **Correlation ID** — trace a specific request across the system
- **From / To** — date-time range pickers

**Table columns:**
| Column | Description |
|--------|-------------|
| Timestamp | Event time (local format) |
| Event Type | Badge showing the audit event type |
| Actor | User or service ID |
| Correlation ID | Request trace identifier (monospace) |

**Row expansion:** Click any row to expand inline detail showing:
- Event ID, Tenant ID, Actor ID, Correlation ID, full timestamp
- Detail payload — pretty-printed JSON if parseable, raw text otherwise

**Pagination:** Previous/Next buttons with page indicator.

### Export Tab

Download audit events as NDJSON (newline-delimited JSON) for offline compliance analysis.

**Filters:**
- Event Type
- Actor ID
- From / To date range

**Download:** Click "Download NDJSON Export" to trigger a browser file download. The file is named `audit-events-{timestamp}.ndjson`.

## API Endpoints Used

| Endpoint | Method | Permission | Purpose |
|----------|--------|------------|---------|
| `/api/audit/events` | GET | `audit:read` | Paginated event query |
| `/api/audit/events/export` | GET | `audit:export` | Streaming NDJSON export |

## Navigation

Accessible from the header nav of the Admin (Connectors) page and Diagnostics page via the "Audit" link.
