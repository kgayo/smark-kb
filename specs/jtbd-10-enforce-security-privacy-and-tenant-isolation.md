# JTBD 10: Enforce Security, Privacy, and Tenant Isolation

## Job to Be Done
As a security/compliance owner, I need strict auth, authorization, auditability, and data protection so support intelligence can operate safely.

## Scope
- Entra ID authentication and RBAC.
- Tenant isolation and security trimming.
- PII handling and redaction controls.
- Audit trail, retention, and deletion workflows.
- Infrastructure-as-code governance for secure and repeatable provisioning.

## Requirements
- Enforce Entra ID auth for all protected endpoints.
- Enforce RBAC roles and per-tenant boundaries across API, retrieval, and admin operations.
- Apply ACL/security trimming before model context assembly.
- Support PII detection and configurable redaction/minimization rules.
- Support a fixed server-side OpenAI API key configured through application settings or equivalent secure server configuration, with controlled rotation and no browser exposure.
- Use Managed Identity for Azure resource access where possible to reduce long-lived secrets.
- Record immutable audit events for queries, retrieval IDs, answers, escalations, and admin changes.
- Support configurable retention and right-to-delete propagation into indexes.
- Retention policy must support configurable detailed-log windows and longer aggregated-metric windows.
- Per-entity retention configuration via `RetentionConfigEntity` (per-tenant, per-entity-type):
  - Supported entity types: `AppSession`, `Message`, `AuditEvent`, `EvidenceChunk`, `AnswerTrace`.
  - `RetentionDays`: detail window (minimum 1 day, no hardcoded default — must be configured per tenant).
  - `MetricRetentionDays`: aggregated-metric window (must be >= `RetentionDays`).
  - `RetentionCleanupService` executes cleanup based on configured policies; compliance verification detects overdue entities.

## Acceptance Criteria
- [x] Cross-tenant access attempts are blocked and audited:
  - `TenantContextMiddleware` extracts `tid` claim from JWT; returns **403 Forbidden** with `TenantMissing` audit event when absent.
  - All EF Core queries apply per-tenant WHERE filters on major entities (sessions, messages, chunks, patterns, connectors, etc.).
  - Resource-level cross-tenant access returns **404 Not Found** (not 403) to prevent tenant-existence information disclosure.
  - ACL/group-based security trimming in retrieval prevents cross-tenant group access.
  - Audit trail tracks attempted cross-tenant access via correlation IDs and actor IDs.
- [x] Restricted documents are never passed to generation layer.
- [x] PII redaction rules are test-covered and auditable.
- [x] Audit exports support investigation and compliance workflows.
- [x] Retention/deletion policy execution is measurable and verifiable.

## Non-Goals
- Legal/compliance policy interpretation inside code.
- Broad data sharing across tenants for model optimization.

## Phase Mapping
- Phase 1: Entra auth + RBAC + baseline security trimming + audit events.
- Phase 2+: expanded privacy controls and compliance tooling.

