# IMPLEMENTATION_PLAN

Last updated: 2026-03-13 (Asia/Manila) — iteration 5 (P0-003 implemented)
Status: Active backlog (P0-001, P0-002, P0-003 complete; remaining items pending)

## Execution Rules
- Always implement highest-priority uncompleted item first.
- Do not skip security/tenant boundaries for feature delivery.
- Do not ship Azure resource changes without corresponding Terraform and ARM template updates.
- Keep changes small, test-backed, and vertically integrated.
- Update this file at end of each Ralph iteration.

## Global Definition of Done
- Behavior implemented and wired end-to-end for the target scope.
- Unit + integration tests added/updated and passing for changed behavior.
- Relevant security checks included (RBAC, tenant isolation, ACL trimming).
- Telemetry added (logs/metrics/traces + correlation IDs) for new critical paths.
- Docs/spec links updated where behavior changed.
- For infrastructure changes, Terraform + ARM templates updated and validated.

## Phase 1 (MVP) Priority Queue

### P0 Foundation and Security (must complete first)
- [x] P0-001: Establish solution skeleton and service boundaries (.NET API, ingestion workers, React app, shared contracts).
  - Specs: jtbd-01, jtbd-05, jtbd-10
  - Exit criteria: runnable backend/frontend skeleton with health endpoints, shared DTO/contract package, and project/solution structure matching AGENTS.md conventions (`src/SmartKb.Api/`, `frontend/`).
  - Completed: Solution skeleton with SmartKb.Api (health endpoints at `/healthz` and `/api/health`), SmartKb.Contracts (AppRole enum, RolePermissions matrix, TenantContext, ApiResponse, HealthStatus DTOs), SmartKb.Ingestion (worker service), React frontend (Vite+TS with ChatPage/AdminPage routes), test projects with baseline tests.

- [x] P0-002: Implement Entra ID authentication + RBAC role model (`SupportAgent`, `SupportLead`, `Admin`, `EngineeringViewer`, `SecurityAuditor`).
  - Specs: jtbd-10
  - Exit criteria: protected endpoints enforce role checks; unauthorized access returns 401/403 and is tested; role claims propagated from Entra tokens; role-to-permission matrix documented in shared contracts.
  - Completed: Microsoft.Identity.Web JWT bearer auth integrated; PermissionRequirement/PermissionAuthorizationHandler maps Entra role claims to AppRole enum and checks RolePermissions matrix; authorization policies auto-generated per permission string; fallback policy requires authentication on all endpoints (health endpoints AllowAnonymous); `/api/me` returns user identity and roles; `/api/admin/connectors` gated on `connector:manage`; `/api/audit/events` gated on `audit:read`; 20 new auth tests (12 unit + 8 integration) covering 401/403 enforcement, role-based access, multi-role, case-insensitive parsing, and anonymous endpoint access; all 46 tests passing.

- [x] P0-003: Implement tenant context propagation and hard tenant isolation across API, retrieval, connector admin, and telemetry.
  - Specs: jtbd-10
  - Exit criteria: tenant ID enforced in all data queries and requests; cross-tenant access tests pass; cross-tenant denial attempts produce immutable audit events (not just HTTP 403); tenant ID attached to correlation context for logs/traces.
  - Completed: ITenantContextAccessor/TenantContextAccessor (scoped DI) extracts tenant from JWT `tid` claim; TenantContextMiddleware enforces tenant presence on all authenticated requests (403 + audit event on missing tenant); AuditEvent model and IAuditEventWriter interface in Contracts; InMemoryAuditEventWriter (placeholder for SQL in P0-005); cross-tenant access on `/api/admin/connectors/{tenantId}` denied with `tenant.cross_access_denied` audit event; tenant ID attached to Activity tags/baggage and log scope for correlation; all endpoints now tenant-scoped; .NET 10 target framework upgrade with package updates; 22 new tests (6 unit middleware + 3 audit writer + 2 contract + 11 integration isolation) covering tenant propagation, cross-tenant denial + audit, case-insensitive matching, missing tenant rejection, and anonymous bypass; all 68 tests passing.

- [ ] P0-004: Implement secret architecture: fixed server-side OpenAI key from application settings, Key Vault for external connector secrets, SQL for secret references only; Managed Identity for Azure service access.
  - Specs: jtbd-01, jtbd-07, jtbd-10
  - Exit criteria: connector credentials resolve from Key Vault; OpenAI key resolves from server-side application settings; SQL stores only external secret refs; Managed Identity used for Key Vault, SQL, Storage, Search, and Service Bus access; raw secrets never logged or returned in API responses.

- [ ] P0-005: Create initial SQL schema + migrations for tenants, users/roles mapping, connectors, sync runs, sessions/messages, feedback, outcomes, audit events.
  - Specs: jtbd-01, jtbd-05, jtbd-06, jtbd-07, jtbd-10
  - Exit criteria: migrations apply cleanly; basic repository tests pass; outcome events table includes resolution type (resolved_without_escalation, escalated, rerouted), target team, acceptance, timing fields, and session ID FK per jtbd-06; audit events table supports immutable append-only writes; soft-delete marker column on content tables (groundwork for Phase 2+ right-to-delete); retention config table with default window values (groundwork for P2-005).

- [ ] P0-005A: Establish baseline IaC for core Azure resources in both Terraform and ARM templates.
  - Specs: jtbd-11
  - Exit criteria: App Service/Container Apps, Azure AI Search, Azure SQL, Azure Blob Storage, Azure Key Vault, Service Bus, and Application Insights resources exist in both Terraform and ARM; environment parameter files for dev/staging/prod created; templates validate cleanly.

- [ ] P0-005B: Add CI validation for Terraform and ARM templates.
  - Specs: jtbd-11
  - Exit criteria: pipeline runs `terraform fmt -check`, `terraform validate`, and `az deployment group validate` for ARM on every infra-affecting PR.

- [ ] P0-005C: Define canonical record schema, ACL filter schema, and select embedding model + chunking parameters.
  - Specs: jtbd-02, jtbd-03, jtbd-10
  - Exit criteria: canonical schema documented (tenant, source, ACL, business metadata, access label fields); ACL filter field names and types defined for Azure AI Search index; embedding model and dimensions selected; chunking size/overlap parameters set; decisions recorded in shared contracts project.
  - Note: Blocking dependency for P0-010, P0-011, P0-012. Must be resolved before ingestion pipeline work begins. Access label must be part of schema so P0-016 Evidence Drawer can display it.

### P0 Ingestion + Evidence Store MVP
- [ ] P0-006: Build connector admin backend endpoints (list/create/edit/enable-disable/test/sync-now) with field mapping, preview/validation, and audit logging.
  - Specs: jtbd-07
  - Exit criteria: admin API contracts implemented and role-gated; field mapping from source schema to canonical schema with transform rules supported; preview endpoint returns sample normalized records; required-field validation errors surfaced before sync activation; credential rotation possible without redeploy; test-connection endpoint returns pass/fail with diagnostic message; OAuth, token/PAT, and private key/service account auth types supported.

- [ ] P0-007: Build ingestion orchestration (queue-backed jobs via Service Bus, retries with exponential backoff, idempotency keys, dead-letter handling, checkpoint tracking).
  - Specs: jtbd-01
  - Exit criteria: replay-safe pipeline with failure recovery tests; dead-letter messages inspectable; checkpoint state persisted per connector/sync run; runtime field mapping failures on unexpected record shapes logged and surfaced (not silently dropped).

- [ ] P0-008: Implement Azure DevOps ingestion (initial backfill + service hook-driven updates + polling fallback).
  - Specs: jtbd-01
  - Exit criteria: ADO wiki pages and work items ingested with source IDs, deep links, timestamps, ACL metadata, and tenant-scoped checkpoints; webhook payload signatures validated; backfill handles 3k+ artifacts without data loss; polling fallback activates when webhooks are unavailable.

- [ ] P0-009: Implement SharePoint ingestion (Graph delta queries + change notifications + fallback polling).
  - Specs: jtbd-01
  - Exit criteria: delta checkpoint sync works, avoids duplicates, and handles token expiry/renewal; change notification subscription managed with lifecycle renewal; polling fallback activates on notification failure.

- [ ] P0-010: Implement canonical normalization + chunking + baseline enrichment (category/module/severity/environment/error tokens).
  - Specs: jtbd-02
  - Exit criteria: normalized/chunked artifacts persisted in Azure Blob Storage with lineage IDs linking chunks to parent source records and tenant; enrichment version tracked for safe reprocessing; metadata supports filterable retrieval; access label computed and stored per canonical record.

- [ ] P0-010A: Set up Azure Blob Storage raw content store for ingested snapshots and extracted text.
  - Specs: jtbd-01, jtbd-02
  - Exit criteria: raw content stored in tenant-scoped blob containers; blob references linked to canonical records in SQL; content accessible for reprocessing.
  - Note: Blob Storage resource provisioned in P0-005A; this item wires the application-level storage layer.

- [ ] P0-011: Implement Azure AI Search Evidence index and indexing pipeline (hybrid-ready fields: vector + keyword + filterable metadata + ACL/tenant filter fields).
  - Specs: jtbd-03
  - Exit criteria: evidence searchable with metadata filters; ACL trim fields indexed; index schema supports vector, keyword, and semantic reranking profiles.

### P0 Retrieval + Orchestration MVP
- [ ] P0-012: Implement hybrid retrieval service (vector + BM25 + semantic rerank), with explicit no-evidence state and telemetry.
  - Specs: jtbd-03
  - Exit criteria: top-k retrieval with RRF-style score fusion; ACL and tenant filters enforced; ACL-filtered-out count returned; no-evidence indicator triggers next-step/escalation path; retrieval telemetry emits top IDs, scores, and trace IDs.

- [ ] P0-013: Implement chat orchestration with structured outputs: answer/next-steps, confidence score + rationale, citations (source IDs + snippets), escalation signal (recommended flag, target team, reason).
  - Specs: jtbd-04
  - Exit criteria: response contract schema-validated; citations required for factual claims; low-confidence responses produce clarifying questions and diagnostic next steps; evidence-to-answer trace links persisted durably in SQL (not just telemetry logs) for audit/compliance queryability; hallucination-prone paths degrade to explicit "I don't know" with next-step guidance rather than generating ungrounded content; session context bounded by configurable token budget with graceful truncation of older messages.

- [ ] P0-013A: Implement session and message persistence API for chat continuity.
  - Specs: jtbd-05
  - Exit criteria: sessions and messages stored in SQL with tenant scope; follow-up questions carry session context; session history retrievable for the owning user; session expiry/max-length policy configurable.

- [ ] P0-014: Enforce "never pass restricted content to model" check in orchestration path.
  - Specs: jtbd-03, jtbd-10
  - Exit criteria: ACL trimming occurs before prompt assembly; restricted documents excluded from model context; integration test proves restricted content never reaches generation layer.

- [ ] P0-014A: Implement baseline PII detection and redaction in orchestration path.
  - Specs: jtbd-10
  - Exit criteria: PII patterns (emails, phone numbers, SSNs, credit cards) detected and redacted/masked before model context assembly; redaction events written to immutable audit event store (not just Application Insights logs); redaction rules test-covered.
  - Note: Baseline scope — advanced policy controls and tenant-configurable rules deferred to P2-001.

- [ ] P0-015: Implement escalation recommendation + structured handoff draft object (without auto-submission).
  - Specs: jtbd-08
  - Exit criteria: response includes target team, reason, severity, suspected component, evidence links, and required handoff fields when escalation thresholds/policies are met; handoff draft reviewable by agent before any external action; all required structured fields (summary, repro steps, logs/IDs requested, suspected component, severity, evidence links) validated as present when determinable.
  - Note: External draft submission to ADO/ClickUp deferred to P1-003. Phase 1 supports copy/export of the draft for manual submission.

### P0 Frontend MVP
- [ ] P0-016: Implement React agent chat with session continuity, confidence badge, citations, and Evidence Drawer.
  - Specs: jtbd-05
  - Exit criteria: user can ask questions and follow up within a session; Evidence Drawer shows snippet, source location, timestamp, and access label per citation; confidence badge displayed on each response.

- [ ] P0-017: Implement next-steps and escalation UX (CTA + handoff draft review/edit + copy/export for manual submission).
  - Specs: jtbd-04, jtbd-05, jtbd-08
  - Exit criteria: escalation CTA visible when escalation signal is present; handoff draft can be reviewed, edited, and copied/exported by agent; next-step guidance displayed for low-confidence responses.
  - Note: Direct ADO/ClickUp submission via UI deferred to P1-003. Phase 1 provides copy-to-clipboard / export flow.

- [ ] P0-018: Implement feedback capture UI + API wiring (thumbs up/down, reason codes, optional correction text, optional corrected-answer proposal).
  - Specs: jtbd-05, jtbd-06
  - Exit criteria: feedback events persist with trace ID and session linkage; reason codes selectable from predefined list; correction text and corrected-answer proposals stored when provided.
  - Note: Corrected-answer proposals are stored but their downstream usage (e.g., gold dataset seeding) is deferred to evaluation harness improvements.

- [ ] P0-018A: Implement outcome tracking API and UI (resolution type, target team, acceptance, time-to-assign, time-to-resolve).
  - Specs: jtbd-06, jtbd-08
  - Exit criteria: outcome events stored in SQL per session with tenant scope; outcomes queryable for reporting; outcome events linked to escalation trace and session ID when applicable; routing quality metrics (acceptance rate, reroute rate per team) computable from stored outcome data.

- [ ] P0-019: Implement admin connectors dashboard baseline (create/edit/test/sync controls + run status + field mapping UI).
  - Specs: jtbd-05, jtbd-07
  - Exit criteria: admin can onboard a connector, test connection, and run sync without redeploy; sync run status and recent errors visible; field mapping configuration available in UI; test-connection action shows pass/fail with diagnostic output; UI routes for admin views enforce role checks client-side (in addition to API-level RBAC).

### P0 Evaluation, SLOs, and Observability MVP
- [ ] P0-020: Instrument OpenTelemetry + correlation IDs across API, ingestion, retrieval, generation, and escalation paths; wire audit event writes at each operation point.
  - Specs: jtbd-06, jtbd-10
  - Exit criteria: traces and logs correlate end-to-end per request/run; correlation IDs propagated through Service Bus messages; Application Insights receives structured telemetry; immutable audit events written for queries, retrieval IDs, answers, escalations, admin changes, cross-tenant denials, and PII redaction events per jtbd-10.

- [ ] P0-020A: Implement audit log query and export API.
  - Specs: jtbd-10
  - Exit criteria: audit events queryable by tenant, date range, event type, and actor; export endpoint produces structured format (NDJSON with pagination) suitable for compliance tooling; role-gated to `SecurityAuditor` and `Admin`.

- [ ] P0-021: Implement baseline evaluation harness (gold dataset format, nightly smoke + weekly full run jobs) with release-gating enforcement.
  - Specs: jtbd-06
  - Exit criteria: gold dataset schema defined; evaluation job produces retrieval precision, generation groundedness, citation coverage, routing accuracy, and no-evidence rate metrics; results stored and comparable against last known good baseline; regression alerts emitted when thresholds degrade; release pipeline blocked or flagged when quality thresholds regress (hard requirement per jtbd-06).

- [ ] P0-022: Implement SLO dashboards and alerts (P95 answer-ready <= 8s, availability >= 99.5%, ingestion lag/error rates).
  - Specs: jtbd-06, jtbd-10
  - Exit criteria: dashboards and alert thresholds active in non-dev env; ingestion lag and dead-letter rate visible; alert routing configured.

## Phase 2 (V1) Priority Queue

- [ ] P1-001: Add HubSpot connector (webhooks with signature validation + ticket API sync fallback) with secure OAuth auth and scope controls.
  - Specs: jtbd-01, jtbd-07
  - Exit criteria: HubSpot tickets ingested with ACL metadata and tenant-scoped checkpoints; webhook signature validation implemented; backfill handles 3k+ artifacts without data loss; polling fallback activates on webhook failure; ACL metadata completeness validated per source-specific model.

- [ ] P1-002: Add ClickUp connector (webhooks with HMAC signature verification + fallback polling).
  - Specs: jtbd-01, jtbd-07
  - Exit criteria: ClickUp docs and tasks ingested with ACL metadata; HMAC signature verification on webhook payloads; backfill handles 3k+ artifacts without data loss; polling fallback tested and activates on webhook failure.

- [ ] P1-003: Implement external draft escalation creation in ADO/ClickUp after human approval.
  - Specs: jtbd-08
  - Exit criteria: agent can submit reviewed handoff draft to create ADO work item or ClickUp task; target system selection logic configurable per tenant; submission audit-logged; UI updated to show submission status.

- [ ] P1-004: Implement Case-Pattern Store index in Azure AI Search and retrieval fusion with Evidence Store.
  - Specs: jtbd-02, jtbd-03, jtbd-09
  - Exit criteria: separate Case-Pattern index in Azure AI Search; retrieval service fuses Evidence Store and Case-Pattern Store results; approved patterns boost response consistency for repeat issues; deprecated patterns excluded from results.

- [ ] P1-005: Implement solved-ticket pattern distillation pipeline (extract canonical problem, root cause, resolution, workaround, verification, escalation playbook hints; draft trust state by default).
  - Specs: jtbd-02, jtbd-09
  - Exit criteria: solved-ticket candidates selected by configurable criteria (D-008); draft patterns created with traceability to source tickets; pattern fields validated; draft patterns visible in governance queue.

- [ ] P1-006: Implement pattern governance workflows (approve/deprecate/supersede + version history + audit trail).
  - Specs: jtbd-09, jtbd-10
  - Exit criteria: lead/admin approval workflow changes trust level; state changes produce immutable audit records; version history and deprecation/replacement links maintained; pattern usage and reuse metrics available.

- [ ] P1-007: Add advanced retrieval filters and tuning controls (product version, environment, tenant-specific synonyms/tokens, semantic relevance profiles).
  - Specs: jtbd-03, jtbd-07

- [ ] P1-008: Expand admin diagnostics (webhook delivery status, rate-limit alerts, dead-letter viewer, field mapping preview, connector health dashboard).
  - Specs: jtbd-07

- [ ] P1-009: Add outcome-driven routing improvement loop (accepted/rerouted/resolved signals feed escalation thresholds/rules; routing quality metrics over time).
  - Specs: jtbd-06, jtbd-08
  - Note: Depends on P0-018A routing quality metrics groundwork.

- [ ] P1-010: Implement IaC drift checks and regular synchronization workflow for Terraform + ARM templates.
  - Specs: jtbd-11

- [ ] P1-011: Implement richer enrichment + case-card quality hardening (deeper category/module/severity extraction, OCR for common binary formats).
  - Specs: jtbd-02

- [ ] P1-012: Implement Terraform remote state backend with state locking and access controls.
  - Specs: jtbd-11
  - Note: Required for safe multi-developer IaC operations; deferred from Phase 1 since dev environment is single-operator initially.

## Phase 3+ (V2+) Priority Queue

- [ ] P2-001: Implement stricter privacy tooling (PII policy controls per tenant, redaction audit workflow, tenant-configurable retention windows, right-to-delete propagation into search indexes).
  - Specs: jtbd-10
  - Note: Depends on soft-delete markers and retention config table established in P0-005.

- [ ] P2-002: Add policy-aware team playbooks and configurable routing policies per tenant.
  - Specs: jtbd-08, jtbd-10

- [ ] P2-003: Add cost optimization controls (token budgets, retrieval compression, embedding cache lifecycle).
  - Specs: jtbd-06

- [ ] P2-004: Expand automation for pattern maintenance and contradiction detection with human review gates.
  - Specs: jtbd-09

- [ ] P2-005: Implement configurable data retention policies with measurable/verifiable execution (detailed-log windows + longer aggregated-metric windows).
  - Specs: jtbd-10
  - Note: Depends on retention config table from P0-005 and soft-delete markers for propagation.

## Cross-Cutting Test Backlog (continuous)
- [ ] T-001: Unit tests for normalization/chunking, structured output parsing, ACL and tenant filters, escalation policy logic, PII redaction rules.
- [ ] T-002: Integration tests for connector contracts (all auth types: OAuth, PAT, private key), webhook signature verification, search indexing/retrieval, OpenAI error handling/retries, Key Vault resolution.
- [ ] T-003: E2E tests for agent journey (answer+citation+feedback+outcome) and admin journey (connect->map->test->sync->validate->query).
- [ ] T-004: Security tests for RBAC, cross-tenant leakage (including audit event generation on denial), restricted-content exclusion from model context, redaction behavior, audit completeness.
- [ ] T-005: Load tests for concurrent chat, ingestion bursts, webhook spikes, and search index write throughput.
- [ ] T-006: IaC tests/validations for Terraform and ARM templates on every infra-affecting change.

## Open Risks / Watch Items
- [ ] R-001: Connector API limits and webhook reliability variability across providers.
- [ ] R-002: Retrieval quality drift as corpus grows; requires active eval + tuning.
- [ ] R-003: Escalation over-triggering or under-triggering before enough outcome data accumulates.
- [ ] R-004: Tenant misconfiguration risk in early environments; require strict guardrails and tests.
- [ ] R-005: Terraform/ARM drift risk if infra updates bypass IaC workflows.
- [ ] R-006: PII leakage into model context before redaction rules are fully mature; baseline detection in Phase 1 mitigates but does not eliminate.
- [ ] R-007: Service Bus message ordering and exactly-once delivery guarantees under high ingestion load; design for idempotency rather than relying on ordering.
- [ ] R-008: Embedding model and chunking parameters unspecified in specs; wrong choice degrades retrieval quality and requires full re-index. Mitigate by benchmarking candidates in P0-005C before committing.
- [ ] R-009: Dual Terraform + ARM maintenance burden; templates may diverge over time. Mitigate with CI validation (P0-005B) and drift checks (P1-010).
- [ ] R-010: Confidence scoring and escalation threshold design not defined in specs; ad-hoc values risk over/under-triggering. Mitigate by treating initial thresholds as tunable config, informed by P0-021 eval runs.
- [ ] R-011: Phase 1 escalation drafts cannot be submitted to external systems (ADO/ClickUp) — only copy/export. Agents must manually create tickets. Communicate this limitation in UX. Mitigate with P1-003.
- [ ] R-012: ACL metadata models differ per source system; incomplete ACL mapping risks either over-permissive or over-restrictive retrieval. Each connector must document and test its ACL mapping decisions.
- [ ] R-013: Session context may exceed model token limits during long conversations; unbounded context degrades quality or causes errors. Mitigate with configurable token budget and graceful truncation in P0-013.

## Open Design Decisions (must resolve before dependent items)
These are ambiguities surfaced during spec review that require explicit decisions before implementation:

- [ ] D-001: Embedding model selection (e.g., text-embedding-3-large dimensions, ada-002) — blocks P0-005C, P0-010, P0-011.
- [ ] D-002: Chunking strategy parameters (token count, overlap, semantic boundary detection) — blocks P0-005C, P0-010.
- [ ] D-003: Confidence scoring methodology (0–1 float, categorical, model self-reported + heuristic blend) — blocks P0-013.
- [ ] D-004: Escalation policy schema and trigger thresholds (confidence threshold, policy rule format, per-tenant vs global) — blocks P0-015.
- [ ] D-005: Top-k value for retrieval and RRF fusion weights — blocks P0-012.
- [ ] D-006: OpenAI model version for generation (GPT-4o, GPT-4-turbo, etc.) — blocks P0-013.
- [ ] D-007: Gold dataset initial population strategy (who authors, review process, minimum case count) — blocks P0-021.
- [ ] D-008: "Solved-ticket candidate" selection criteria for case-card extraction (status field, label, threshold) — blocks P1-005.
- [ ] D-009: Terraform remote state backend choice (Azure Storage, Terraform Cloud) — blocks P1-012.
- [ ] D-010: Session context token budget and truncation strategy (sliding window, summarization, hard cutoff) — blocks P0-013, P0-013A.
- [ ] D-011: Audit export format and pagination scheme (NDJSON, CSV, cursor-based vs offset) — blocks P0-020A.
- [ ] D-012: "No-evidence" threshold definition (minimum score, minimum result count, or both) — blocks P0-012.
- [ ] D-013: Hallucination degradation strategy (refuse + next-steps, caveated answer, escalation trigger) — blocks P0-013.

## Plan Maintenance Checklist (run each iteration)
- [ ] Mark completed items with evidence (PR/test IDs).
- [ ] Reorder remaining items by risk and dependency.
- [ ] Add newly discovered bugs/tech debt with priority.
- [ ] Confirm Terraform and ARM templates remain synchronized after Azure resource changes.
- [ ] Remove stale completed details to keep plan concise.
