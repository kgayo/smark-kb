# API Reference

Smart KB exposes 123 endpoints across 21 functional groups. All endpoints (except webhooks and health) require authentication and return wrapped `ApiResponse<T>` responses with `Success`, `Data`, `Error`, and `CorrelationId` fields.

## Authorization Model

| Role | Permissions |
|------|-------------|
| **SupportAgent** | `chat:query`, `chat:feedback`, `chat:outcome`, `session:read_own` |
| **SupportLead** | Above + `session:read_team`, `pattern:approve`, `pattern:deprecate`, `report:read` |
| **Admin** | All above + `connector:manage`, `connector:sync`, `audit:read`, `audit:export`, `tenant:manage`, `privacy:manage` |
| **EngineeringViewer** | `report:read`, `pattern:read` |
| **SecurityAuditor** | `audit:read`, `audit:export`, `report:read` |

---

## Health & Status

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/` | Public | Root status |
| GET | `/api/health` | Public | Service health with version |
| GET | `/healthz` | Public | Health checks |

## User Context

| Method | Path | Permission | Description |
|--------|------|------------|-------------|
| GET | `/api/me` | Authenticated | Current user profile (roles, tenant, correlation ID) |

## Chat & Sessions

| Method | Path | Permission | Description |
|--------|------|------------|-------------|
| POST | `/api/sessions` | `chat:query` | Create session |
| GET | `/api/sessions` | `chat:query` | List user sessions |
| GET | `/api/sessions/{id}` | `chat:query` | Get session details |
| DELETE | `/api/sessions/{id}` | `chat:query` | Delete session |
| GET | `/api/sessions/{id}/messages` | `chat:query` | List messages |
| POST | `/api/sessions/{id}/messages` | `chat:query` | Send chat message |
| POST | `/api/chat` | `chat:query` | Stateless chat (legacy) |

## Feedback

| Method | Path | Permission | Description |
|--------|------|------------|-------------|
| POST | `/api/sessions/{sid}/messages/{mid}/feedback` | `chat:feedback` | Submit feedback |
| GET | `/api/sessions/{sid}/messages/{mid}/feedback` | `chat:feedback` | Get feedback |
| GET | `/api/sessions/{sid}/feedbacks` | `chat:feedback` | List session feedbacks |

## Outcomes

| Method | Path | Permission | Description |
|--------|------|------------|-------------|
| POST | `/api/sessions/{id}/outcome` | `chat:outcome` | Record outcome |
| GET | `/api/sessions/{id}/outcome` | `chat:outcome` | Get outcome |

## Escalation Drafts

| Method | Path | Permission | Description |
|--------|------|------------|-------------|
| POST | `/api/escalations/draft` | `chat:query` | Create draft |
| GET | `/api/escalations/draft/{id}` | `chat:query` | Get draft |
| GET | `/api/sessions/{id}/escalations/drafts` | `chat:query` | List session drafts |
| PUT | `/api/escalations/draft/{id}` | `chat:query` | Update draft |
| GET | `/api/escalations/draft/{id}/export` | `chat:query` | Export as Markdown |
| POST | `/api/escalations/draft/{id}/approve` | `chat:query` | Approve and create |
| DELETE | `/api/escalations/draft/{id}` | `chat:query` | Delete draft |

## Webhooks (Source System Callbacks)

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| POST | `/api/webhooks/ado/{connectorId}` | Signature | Azure DevOps webhook |
| POST | `/api/webhooks/msgraph/{connectorId}` | clientState | SharePoint/Graph webhook |
| POST | `/api/webhooks/hubspot/{connectorId}` | HMAC-SHA256 | HubSpot webhook |
| POST | `/api/webhooks/clickup/{connectorId}` | HMAC-SHA256 | ClickUp webhook |

## Connector Admin

| Method | Path | Permission | Description |
|--------|------|------------|-------------|
| GET | `/api/admin/connectors` | `connector:manage` | List connectors |
| POST | `/api/admin/connectors` | `connector:manage` | Create connector |
| GET | `/api/admin/connectors/{id}` | `connector:manage` | Get connector |
| PUT | `/api/admin/connectors/{id}` | `connector:manage` | Update connector |
| DELETE | `/api/admin/connectors/{id}` | `connector:manage` | Delete connector |
| POST | `/api/admin/connectors/{id}/enable` | `connector:manage` | Enable |
| POST | `/api/admin/connectors/{id}/disable` | `connector:manage` | Disable |
| POST | `/api/admin/connectors/{id}/test` | `connector:manage` | Test credentials |
| POST | `/api/admin/connectors/{id}/sync-now` | `connector:manage` | Trigger sync |
| POST | `/api/admin/connectors/{id}/preview` | `connector:manage` | Preview data |
| POST | `/api/admin/connectors/{id}/validate-mapping` | `connector:manage` | Validate mappings |
| GET | `/api/admin/connectors/{id}/sync-runs` | `connector:manage` | Sync history |
| GET | `/api/admin/connectors/{id}/sync-runs/{rid}` | `connector:manage` | Sync run details |

## Synonym Rules

| Method | Path | Permission | Description |
|--------|------|------------|-------------|
| GET | `/api/admin/synonym-rules` | `connector:manage` | List rules |
| GET | `/api/admin/synonym-rules/{id}` | `connector:manage` | Get rule |
| POST | `/api/admin/synonym-rules` | `connector:manage` | Create rule |
| PUT | `/api/admin/synonym-rules/{id}` | `connector:manage` | Update rule |
| DELETE | `/api/admin/synonym-rules/{id}` | `connector:manage` | Delete rule |
| POST | `/api/admin/synonym-rules/sync` | `connector:manage` | Sync to Azure AI Search |
| POST | `/api/admin/synonym-rules/seed` | `connector:manage` | Load default seeds |

## Retrieval Tuning

| Method | Path | Permission | Description |
|--------|------|------------|-------------|
| GET | `/api/admin/retrieval-settings` | `connector:manage` | Get settings |
| PUT | `/api/admin/retrieval-settings` | `connector:manage` | Update settings |
| DELETE | `/api/admin/retrieval-settings` | `connector:manage` | Reset to defaults |

## Pattern Governance

| Method | Path | Permission | Description |
|--------|------|------------|-------------|
| GET | `/api/admin/patterns/candidates` | `pattern:approve` | Find distillation candidates |
| POST | `/api/admin/patterns/distill` | `pattern:approve` | Auto-distill patterns |
| GET | `/api/patterns/governance-queue` | `pattern:approve` | Governance queue |
| GET | `/api/patterns/{id}` | `pattern:approve` | Get pattern |
| POST | `/api/patterns/{id}/review` | `pattern:approve` | Review pattern |
| POST | `/api/patterns/{id}/approve` | `pattern:approve` | Approve (auto-indexes) |
| POST | `/api/patterns/{id}/deprecate` | `pattern:deprecate` | Deprecate (removes from index) |

## Pattern Maintenance

| Method | Path | Permission | Description |
|--------|------|------------|-------------|
| POST | `/api/admin/patterns/detect-contradictions` | `pattern:approve` | Run contradiction detection |
| GET | `/api/admin/patterns/contradictions` | `pattern:approve` | List contradictions |
| POST | `/api/admin/patterns/contradictions/{id}/resolve` | `pattern:approve` | Resolve contradiction |
| POST | `/api/admin/patterns/detect-maintenance` | `pattern:approve` | Detect stale/low-quality patterns |
| GET | `/api/admin/patterns/maintenance-tasks` | `pattern:approve` | List maintenance tasks |
| POST | `/api/admin/patterns/maintenance-tasks/{id}/resolve` | `pattern:approve` | Resolve task |
| POST | `/api/admin/patterns/maintenance-tasks/{id}/dismiss` | `pattern:approve` | Dismiss task |

## Diagnostics

| Method | Path | Permission | Description |
|--------|------|------------|-------------|
| GET | `/api/admin/slo/status` | `connector:manage` | SLO targets |
| GET | `/api/admin/secrets/status` | `connector:manage` | Secrets config status |
| GET | `/api/admin/diagnostics/summary` | `connector:manage` | Full system diagnostics |
| GET | `/api/admin/connectors/{id}/webhooks` | `connector:manage` | Connector webhook status |
| GET | `/api/admin/webhooks` | `connector:manage` | All webhook statuses |
| GET | `/api/admin/ingestion/dead-letters` | `connector:manage` | Dead-letter queue peek |

## Routing Rules & Analytics

| Method | Path | Permission | Description |
|--------|------|------------|-------------|
| GET | `/api/admin/routing-rules` | `connector:manage` | List rules |
| GET | `/api/admin/routing-rules/{id}` | `connector:manage` | Get rule |
| POST | `/api/admin/routing-rules` | `connector:manage` | Create rule |
| PUT | `/api/admin/routing-rules/{id}` | `connector:manage` | Update rule |
| DELETE | `/api/admin/routing-rules/{id}` | `connector:manage` | Delete rule |
| GET | `/api/admin/routing/analytics` | `connector:manage` | Routing analytics |
| POST | `/api/admin/routing/recommendations/generate` | `connector:manage` | Generate recommendations |
| GET | `/api/admin/routing/recommendations` | `connector:manage` | List recommendations |
| POST | `/api/admin/routing/recommendations/{id}/apply` | `connector:manage` | Apply recommendation |
| POST | `/api/admin/routing/recommendations/{id}/dismiss` | `connector:manage` | Dismiss recommendation |

## Team Playbooks

| Method | Path | Permission | Description |
|--------|------|------------|-------------|
| GET | `/api/admin/playbooks` | `connector:manage` | List playbooks |
| GET | `/api/admin/playbooks/{id}` | `connector:manage` | Get playbook |
| GET | `/api/admin/playbooks/team/{name}` | `connector:manage` | Get by team |
| POST | `/api/admin/playbooks` | `connector:manage` | Create playbook |
| PUT | `/api/admin/playbooks/{id}` | `connector:manage` | Update playbook |
| DELETE | `/api/admin/playbooks/{id}` | `connector:manage` | Delete playbook |
| POST | `/api/admin/playbooks/validate` | `connector:manage` | Validate draft |

## Privacy & Data Protection

| Method | Path | Permission | Description |
|--------|------|------------|-------------|
| GET | `/api/admin/privacy/pii-policy` | `privacy:manage` | Get PII policy |
| PUT | `/api/admin/privacy/pii-policy` | `privacy:manage` | Upsert PII policy |
| DELETE | `/api/admin/privacy/pii-policy` | `privacy:manage` | Reset PII policy |
| GET | `/api/admin/privacy/retention` | `privacy:manage` | Get retention policies |
| PUT | `/api/admin/privacy/retention` | `privacy:manage` | Upsert retention policy |
| DELETE | `/api/admin/privacy/retention/{entityType}` | `privacy:manage` | Delete retention policy |
| POST | `/api/admin/privacy/retention/cleanup` | `privacy:manage` | Execute cleanup |
| POST | `/api/admin/privacy/data-subject-deletion` | `privacy:manage` | Request deletion |
| GET | `/api/admin/privacy/data-subject-deletion` | `privacy:manage` | List deletion requests |
| GET | `/api/admin/privacy/data-subject-deletion/{id}` | `privacy:manage` | Get request details |
| GET | `/api/admin/privacy/retention/history` | `privacy:manage` | Execution history |
| GET | `/api/admin/privacy/retention/compliance` | `privacy:manage` | Compliance report |

## Cost Optimization

| Method | Path | Permission | Description |
|--------|------|------------|-------------|
| GET | `/api/admin/cost-settings` | `connector:manage` | Get token budget |
| PUT | `/api/admin/cost-settings` | `connector:manage` | Update budget |
| DELETE | `/api/admin/cost-settings` | `connector:manage` | Reset to defaults |
| GET | `/api/admin/token-usage/summary` | `connector:manage` | Usage summary |
| GET | `/api/admin/token-usage/daily` | `connector:manage` | Daily breakdown |
| GET | `/api/admin/token-usage/budget-check` | `connector:manage` | Budget status |

## Search Index Migrations

| Method | Path | Permission | Description |
|--------|------|------------|-------------|
| GET | `/api/admin/index-migrations/{type}/current` | `connector:manage` | Current schema version |
| GET | `/api/admin/index-migrations/{type}/versions` | `connector:manage` | Version history |
| GET | `/api/admin/index-migrations/{type}/plan` | `connector:manage` | Plan migration |
| POST | `/api/admin/index-migrations/{type}/bootstrap` | `connector:manage` | Initialize tracking |
| POST | `/api/admin/index-migrations/{type}/execute` | `connector:manage` | Execute migration |
| POST | `/api/admin/index-migrations/{type}/rollback` | `connector:manage` | Rollback |
| DELETE | `/api/admin/index-migrations/retired/{id}` | `connector:manage` | Cleanup retired |

## Eval Reports

| Method | Path | Permission | Description |
|--------|------|------------|-------------|
| GET | `/api/admin/eval/reports` | `connector:manage` | List eval reports (paginated, `?runType=smoke\|full&page=1&pageSize=20`) |
| GET | `/api/admin/eval/reports/{id}` | `connector:manage` | Get eval report detail with metrics/violations/baseline |
| POST | `/api/admin/eval/reports` | `connector:manage` | Persist eval report from harness run |

## Gold Dataset

| Method | Path | Permission | Description |
|--------|------|------------|-------------|
| GET | `/api/admin/eval/gold-cases` | `connector:manage` | List gold cases (paginated, `?tag=auth&page=1&pageSize=20`) |
| GET | `/api/admin/eval/gold-cases/{id}` | `connector:manage` | Get gold case detail |
| POST | `/api/admin/eval/gold-cases` | `connector:manage` | Create gold case |
| PUT | `/api/admin/eval/gold-cases/{id}` | `connector:manage` | Update gold case |
| DELETE | `/api/admin/eval/gold-cases/{id}` | `connector:manage` | Delete gold case |
| GET | `/api/admin/eval/gold-cases/export` | `connector:manage` | Export as NDJSON for eval CLI |
| POST | `/api/admin/eval/gold-cases/promote` | `connector:manage` | Promote from user feedback |

## Audit

| Method | Path | Permission | Description |
|--------|------|------------|-------------|
| GET | `/api/audit/events` | `audit:read` | Query audit events |
| GET | `/api/audit/events/export` | `audit:export` | Export as NDJSON |
