# Privacy & Data Protection Page

**Route:** `/privacy`
**Required Role:** Admin
**Component:** `PrivacyAdminPage.tsx`

Configure PII handling policies, manage data retention, process data subject deletion requests, and monitor retention compliance.

## Tabs

### PII Policy Tab

Per-tenant PII detection and redaction configuration.

| Setting | Description |
|---------|-------------|
| Enforcement Mode | `redact` (remove PII before model), `detect` (log only), `disabled` |
| Enabled PII Types | Checkboxes: email, phone, SSN, credit card |
| Audit Redactions | Log each redaction event to audit trail |
| Custom Patterns | Named regex patterns with custom placeholders |

**Custom Patterns**: add patterns with a name, regex, and replacement placeholder (e.g., name: "Employee ID", regex: `EMP-\d{6}`, placeholder: `[REDACTED-EMPID]`).

Actions: Configure/Edit, Reset to Defaults.

### Retention Tab

Per-entity-type data retention policies.

**Add/Update Policy**: select entity type (AppSession, Message, AuditEvent, EvidenceChunk, AnswerTrace), set retention days, optional metric retention days (must be >= retention days).

**Policies Table**:

| Column | Description |
|--------|-------------|
| Entity Type | The data type governed |
| Retention Days | Days before detailed records are purged |
| Metric Days | Days before aggregated metrics are purged |
| Last Updated | When the policy was last modified |

**Run Cleanup Now**: execute retention cleanup immediately. Results show per-entity deletion counts and cutoff dates.

Actions: Delete individual policies.

### Data Deletion Tab

GDPR / data subject deletion request management.

**Submit Request**: enter a subject/user ID to initiate deletion across all entity types (sessions, messages, feedbacks, answer traces, escalation drafts, outcome events).

**Requests Table**:

| Column | Description |
|--------|-------------|
| Request ID | Unique identifier (truncated) |
| Subject ID | The data subject whose data was deleted |
| Status | Completed, Failed, or In Progress |
| Requested | When the request was submitted |
| Completed | When processing finished |

Click a row to see the deletion summary (records deleted per entity type) or error detail.

### Compliance Tab

Retention policy compliance monitoring.

**Summary Card**: overall Compliant / Non-Compliant status with total and overdue policy counts.

**Compliance Entries Table**:

| Column | Description |
|--------|-------------|
| Entity Type | The data type |
| Retention Days | Configured retention window |
| Last Executed | When cleanup last ran |
| Days Since | Days since last execution |
| Last Deleted | Records deleted in last run |
| Status | OK or Overdue (configurable window, default 7 days) |

## API Endpoints Used

- `GET /api/admin/privacy/pii-policy` — get PII policy
- `PUT /api/admin/privacy/pii-policy` — update PII policy
- `DELETE /api/admin/privacy/pii-policy` — reset PII policy
- `GET /api/admin/privacy/retention` — list retention policies
- `PUT /api/admin/privacy/retention` — upsert retention policy
- `DELETE /api/admin/privacy/retention/{entityType}` — delete policy
- `POST /api/admin/privacy/retention/cleanup` — execute cleanup
- `GET /api/admin/privacy/retention/history` — execution history
- `GET /api/admin/privacy/retention/compliance` — compliance report
- `POST /api/admin/privacy/data-subject-deletion` — submit deletion request
- `GET /api/admin/privacy/data-subject-deletion` — list requests
- `GET /api/admin/privacy/data-subject-deletion/{id}` — request detail
