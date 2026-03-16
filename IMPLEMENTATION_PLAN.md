# IMPLEMENTATION_PLAN

Last updated: 2026-03-16 (Asia/Manila) — iteration 24 (P0-010 complete)
Status: Active backlog (P0-001 through P0-010 complete; 0 bugs blocking, 0 tech-debt blocking; next up P0-010A)

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

### P0 Foundation and Security (complete)
- [x] P0-001: Establish solution skeleton and service boundaries (.NET API, ingestion workers, React app, shared contracts).
  - Specs: jtbd-01, jtbd-05, jtbd-10
  - Completed: Solution skeleton with SmartKb.Api, SmartKb.Contracts, SmartKb.Ingestion, SmartKb.Data, React frontend, 4 test projects.

- [x] P0-002: Implement Entra ID authentication + RBAC role model.
  - Specs: jtbd-10
  - Completed: Microsoft.Identity.Web JWT bearer auth; PermissionRequirement/PermissionAuthorizationHandler; role-to-permission matrix with 14 permission strings across 5 roles; fallback policy requires authentication; 20 auth tests.

- [x] P0-003: Implement tenant context propagation and hard tenant isolation.
  - Specs: jtbd-10
  - Completed: TenantContextMiddleware extracts `tid` claim; 403 + audit on missing tenant; correlation IDs on Activity tags/baggage; cross-tenant audit events; 22 tests. BUG-001 fixed (iteration 16).

- [x] P0-004: Implement secret architecture.
  - Specs: jtbd-01, jtbd-07, jtbd-10
  - Completed: ISecretProvider with KeyVaultSecretProvider (DefaultAzureCredential); OpenAiKeyProvider; SecretMaskingExtensions; diagnostic endpoint; 19 tests.

- [x] P0-005: Create initial SQL schema + migrations.
  - Specs: jtbd-01, jtbd-05, jtbd-06, jtbd-07, jtbd-10
  - Completed: SmartKb.Data with EF Core; 10 entity classes; InitialCreate migration; SqlAuditEventWriter; global query filters for soft-delete; 18 data tests.

- [x] P0-005A: Establish baseline IaC for core Azure resources.
  - Specs: jtbd-11
  - Completed: Terraform (modular .tf files) + ARM (main.json) with full resource parity; env-specific parameter files. Known issue: ARM missing SQL Entra administrator (see BUG-002).

- [x] P0-005B: Add CI validation for Terraform and ARM templates.
  - Specs: jtbd-11
  - Completed: GitHub Actions with terraform fmt/validate + ARM JSON/structure validation + conditional az deployment validate.

- [x] P0-005C: Define canonical record schema, ACL filter schema, embedding + chunking config.
  - Specs: jtbd-02, jtbd-03, jtbd-10
  - Completed: CanonicalRecord, EvidenceChunk, SearchFieldNames, EmbeddingSettings (text-embedding-3-large@1536), ChunkingSettings (512/64); 23 tests.

### P0 Ingestion + Evidence Store MVP
- [x] P0-006: Build connector admin backend endpoints.
  - Specs: jtbd-07
  - Completed: 14 endpoints for connector CRUD, field mapping validation, preview, sync-now, sync-runs; audit events on all mutations; 32 integration + 23 DTO tests.

- [x] P0-007: Build ingestion orchestration with Service Bus queue, retries, idempotency, and checkpoint tracking.
  - Specs: jtbd-01
  - Completed: SyncJobMessage, ServiceBusSyncJobPublisher, SyncJobProcessor (Pending->Running->Completed/Failed, multi-batch, checkpoint persistence, error capping), IngestionWorker (Service Bus processor with DLQ handling), dead-letter peek endpoint; 16 new tests; all 228 tests passing.

### Bugs and Tech Debt (fix before proceeding with new features)

- [x] BUG-001: Fix 5 pre-existing TenantIsolation test failures (cross-tenant route design issue).
  - Completed: Rewrote 8 tests into 11 well-scoped tests. Replaced path-based tenant ID tests with connector-seeding approach (create in tenant A, access from tenant B JWT → 404). Fixed `AuthTestFactory` to register `InMemoryAuditEventWriter` as singleton for both `IAuditEventWriter` and concrete type. Fixed `AdminConnectors_ReturnsTenantId` to validate `ApiResponse` structure. Added `CrossTenantConnectorAccess_DoesNotLeakExistence` test. All 233 tests passing.

- [x] BUG-002: ARM/Terraform misalignment — ARM missing Entra administrator for SQL Server.
  - Root cause: `infra/terraform/sql.tf` has `azuread_administrator {}` block wiring the App Service managed identity as SQL admin. `infra/arm/main.json` had no corresponding `Microsoft.Sql/servers/administrators` resource.
  - Completed: Added `Microsoft.Sql/servers/administrators/ActiveDirectory` resource to ARM template matching Terraform behavior (login=smartkb-admin, sid=App Service managed identity principal, tenantId=entraTenantId). JSON validated.

- [x] BUG-003: `ConnectorSecretReference` model is orphaned.
  - Root cause: Model defined in `SmartKb.Contracts/Models/ConnectorSecretReference.cs` with fields for ConnectorId, TenantId, AuthType, KeyVaultSecretName, CreatedAt, RotatedAt. But no SQL entity, migration column, repository, or API surface references it.
  - Completed: Removed orphaned `ConnectorSecretReference` record and 3 associated tests. Can be re-added if secret rotation tracking is needed for P0-008/P0-009 connectors. All 233 tests passing.

- [x] TECH-001: Add Ingestion Worker App Service to Terraform and ARM templates.
  - Root cause: Only the API web app (`app-smartkb-api-{env}`) is provisioned in IaC. The Ingestion Worker needs its own App Service (or Container App) resource for production deployment.
  - Completed: Added `app-smartkb-ingestion-{env}` Linux Web App to both Terraform (`app-service.tf`) and ARM (`main.json`). Shares same App Service Plan as API. SystemAssigned managed identity with Key Vault Secrets User role and Azure Service Bus Data Receiver role. App settings: Application Insights + Key Vault URI. Connection string: SmartKbDb with Active Directory Default auth. ARM template validated (19 resources). All 233 tests passing.

- [x] TECH-002: Service Bus should use Managed Identity instead of connection string.
  - Root cause: `ServiceBusSyncJobPublisher` and `IngestionWorker` both use `ServiceBusClient(connectionString)`. Per jtbd-10 R6 and AGENTS.md, Managed Identity should be preferred.
  - Completed: Added `FullyQualifiedNamespace` to `ServiceBusSettings` (preferred over `ConnectionString`). Both API and Ingestion `Program.cs` now create `ServiceBusClient` with `DefaultAzureCredential` when namespace is set. Added `ServiceBus__FullyQualifiedNamespace` app setting to both App Services in Terraform and ARM. Added `Azure Service Bus Data Sender` role for API app (was missing). Added `IsConfigured`/`UsesManagedIdentity` helpers. 4 new tests; all 236 tests passing. ARM template validated (20 resources, 13 outputs).

- [x] TECH-003: Duplicate `KeyVaultSecretProvider` in API and Ingestion projects.
  - Root cause: `src/SmartKb.Api/Secrets/KeyVaultSecretProvider.cs` and `src/SmartKb.Ingestion/Secrets/KeyVaultSecretProvider.cs` are identical copies.
  - Completed: Moved single implementation to `SmartKb.Contracts/Services/KeyVaultSecretProvider.cs`. Added `Azure.Security.KeyVault.Secrets` and `Microsoft.Extensions.Logging.Abstractions` to Contracts csproj. Deleted both duplicate files. All 233 tests passing.

- [x] ~~TECH-004~~: Soft-deleted connectors block name reuse — **CLOSED (not a bug)**.
  - Resolution: EF Core global query filter `HasQueryFilter(c => c.DeletedAt == null)` on `ConnectorEntity` applies automatically to all queries on `_db.Connectors`, including the duplicate name checks in `CreateAsync` (line 80) and `UpdateAsync` (line 124). Soft-deleted connectors are already excluded. Verified 2026-03-16.

- [x] TECH-005: `SetStatusAsync` vs `EnableAsync` inconsistency in ConnectorAdminService.
  - Root cause: `SetStatusAsync` had a silent validation failure path (returns `(true, null)` when validation fails), while `EnableAsync` returns the validation result explicitly. The `/disable` route used `SetStatusAsync`.
  - Completed: Replaced `SetStatusAsync` with dedicated `DisableAsync` method. Updated `/disable` route to call `DisableAsync` directly. `DisableAsync` has clean return type `(Found, Response)` with no unnecessary validation path. All 233 tests passing.

- [x] TECH-006: No CI pipeline for .NET build/test or frontend build.
  - Completed: Added `.github/workflows/ci.yml` with two jobs — `dotnet` (restore/build/test in Release) and `frontend` (npm ci/lint/build). Triggers on PRs to main and pushes to main.

- [x] ~~TECH-007~~: Stale `src/src.sln` file in repo root — **CLOSED (not found)**.
  - Resolution: No `src/src.sln` file exists in the repository. Only `SmartKb.sln` at the root. Verified 2026-03-16.

### P0 Ingestion + Evidence Store MVP (continued)

- [x] P0-008: Implement Azure DevOps ingestion (initial backfill + incremental sync).
  - Specs: jtbd-01
  - Completed: First concrete `IConnectorClient` implementation — `AzureDevOpsConnectorClient` in `SmartKb.Contracts.Connectors`. Supports PAT auth via Key Vault. Ingests work items (WIQL API) and wiki pages (Wiki REST API). ACL mapping: area paths → `AllowedGroups` with `Restricted` visibility; wiki pages default to `Internal`. Checkpoint-based multi-project, multi-phase sync (`AdoCheckpoint` tracks project index + phase + last-modified timestamp). Deep links to work items and wiki pages. Content hash for dedup. HTML stripping for work item descriptions. Handles 3k+ artifacts via batched WIQL (200/batch) with `HasMore` pagination. Per-project error isolation (auth failure in one project doesn't crash the sync). `AzureDevOpsSourceConfig` model for organization URL, project list, work item type/area path filters, batch size. Registered in both API and Ingestion DI. 40 new tests (27 unit + 7 integration + 6 ADO sync processor tests); all 273 tests passing. Webhook support deferred to P0-008A (requires webhook receiver endpoint + signature verification).

- [x] P0-008A: Add ADO service hook webhook support (event-driven freshness + polling fallback).
  - Specs: jtbd-01
  - Dependencies: P0-008 (complete)
  - Completed: `WebhookSubscriptionEntity` with SQL table, unique index on (ConnectorId, EventType). `IWebhookManager` interface with `AdoWebhookManager` implementation — registers `workitem.created` and `workitem.updated` service hooks via ADO REST API (v7.1). Webhook secret generated (32-byte random), stored in Key Vault via `ISecretProvider.SetSecretAsync`. `AdoWebhookHandler` processes incoming payloads: validates HMAC signature (Basic auth shared secret), deduplicates via idempotency key, triggers incremental sync via Service Bus. Anonymous endpoint `POST /api/webhooks/ado/{connectorId}`. Webhook lifecycle managed in `ConnectorAdminService`: register on enable, deregister on disable. `WebhookPollingFallbackService` (BackgroundService) checks every 30s for subscriptions in fallback mode; triggers incremental syncs at 5-minute intervals with 0-60s jitter. Failure threshold: 3 consecutive failures activates fallback. `WebhookSettings` configurable via `Webhook:*` app settings. EF Core migration `AddWebhookSubscriptions`. 36 new tests (8 signature, 11 handler, 4 endpoint, 9 webhook manager, 4 polling fallback); all 309 tests passing.

- [x] P0-009: Implement SharePoint ingestion (Graph delta queries + change notifications + fallback polling).
  - Specs: jtbd-01
  - Dependencies: P0-007 (complete)
  - Completed: Second `IConnectorClient` implementation — `SharePointConnectorClient` in `SmartKb.Contracts.Connectors`. Uses Microsoft Graph REST API (no SDK dependency) with OAuth2 client credentials flow for authentication. Ingests document library files via delta queries (`/drives/{id}/root/delta`). Delta token checkpoint tracks multi-drive incremental sync position. Handles delta token expiry (410 Gone) by falling back to full sync automatically. File extension filtering (supported: .txt, .md, .pdf, .docx, .pptx, .xlsx, etc.) and folder exclusion. ACL mapping: drive name → `AllowedGroups` with `Restricted` visibility. Content hash for dedup based on item metadata (full text extraction deferred to P0-010). `SharePointSourceConfig` model with site URL, Entra ID tenant ID, client ID, drive IDs, extension/folder filters, batch size. `SharePointWebhookManager` registers Graph change notification subscriptions per drive (max 4230-minute lifetime). `SharePointWebhookHandler` processes Graph change notifications with clientState validation (constant-time comparison) and validation handshake support. Anonymous endpoint `POST /api/webhooks/msgraph/{connectorId}` with `?validationToken=` handshake. Webhook lifecycle managed in `ConnectorAdminService`: register on enable, deregister on disable. Reuses existing `WebhookPollingFallbackService` for failure recovery (3 consecutive failures threshold). `GraphChangeNotificationPayload`, `GraphSubscriptionRequest/Response` models for Graph webhook protocol. Registered in both API and Ingestion DI. 55 new tests (27 connector client unit + 8 webhook manager + 13 webhook handler + 5 endpoint integration + 2 checkpoint); all 364 tests passing.

- [x] P0-010: Implement canonical normalization + chunking + baseline enrichment.
  - Specs: jtbd-02
  - Dependencies: P0-008 or P0-009 (need at least one connector producing records)
  - Completed: `IChunkingService` + `TextChunkingService` (markdown structural boundaries, 512 tokens/chunk, 64 overlap, paragraph fallback, hard-split for oversized sections). `IEnrichmentService` + `BaselineEnrichmentService` (keyword-based category/severity/environment detection, error token extraction via regex — exception names, HTTP status codes, error codes, hex codes — baseline PII detection for emails/phones/SSNs/credit cards). `INormalizationPipeline` + `NormalizationPipeline` orchestrates chunking→enrichment→EvidenceChunk production with full lineage (ChunkId = `{EvidenceId}_chunk_{index}`). `EvidenceChunkEntity` in SQL with `AddEvidenceChunks` migration (tenant-scoped, connector FK, content hash for dedup, enrichment version tracking, reprocessed-at timestamp). `SyncJobProcessor` now runs normalization pipeline after each fetch batch and persists chunks via upsert. `ErrorTokens` field added to `CanonicalRecord`. `EnrichmentVersion` and `ErrorTokens` fields added to `EvidenceChunk`. Services registered in both API and Ingestion DI. 46 new tests (9 chunking, 19 enrichment, 13 pipeline, 5 existing test updates); all 410 tests passing. SPEC-005 (ErrorTokens) and SPEC-006 (chunk ID format) resolved.

- [ ] P0-010A: Set up Azure Blob Storage raw content store for ingested snapshots and extracted text.
  - Specs: jtbd-01, jtbd-02
  - Dependencies: P0-005A (Blob resource provisioned in IaC)
  - Exit criteria: raw content stored in tenant-scoped blob containers; blob references linked to canonical records in SQL; content accessible for reprocessing.
  - Implementation notes: `raw-content` container already provisioned in IaC. Need `IBlobStorageService` interface, tenant-scoped path convention (`{tenantId}/{connectorType}/{evidenceId}/raw`), and blob reference column on a SQL entity.

- [ ] P0-011: Implement Azure AI Search Evidence index and indexing pipeline.
  - Specs: jtbd-03
  - Dependencies: P0-010 (chunks must exist to index)
  - Exit criteria: evidence searchable with metadata filters; ACL trim fields indexed; index schema supports vector, keyword, and semantic reranking profiles.
  - Implementation notes: Index schema from SearchFieldNames (P0-005C). Need `IIndexingService` interface. Semantic configuration must specify which fields are used for semantic reranking. Vector profile for embedding_vector at 1536 dims. Index creation should be idempotent (create-or-update).

### P0 Retrieval + Orchestration MVP

- [ ] P0-012: Implement hybrid retrieval service (vector + BM25 + semantic rerank).
  - Specs: jtbd-03
  - Dependencies: P0-011 (index must exist), **D-005 (top-k/RRF weights — must resolve)**, **D-012 (no-evidence threshold — must resolve)**
  - Exit criteria: top-k retrieval with RRF-style score fusion; ACL and tenant filters enforced; ACL-filtered-out count returned; no-evidence indicator triggers next-step/escalation path; retrieval telemetry emits top IDs, scores, and trace IDs.
  - Design decisions to resolve before implementation:
    - **D-005**: Propose top-k=20 (Evidence), RRF weight 1.0 for both BM25 and vector (equal weighting as baseline). Phase 1 uses Evidence Index only; Pattern Index fusion deferred to P1-004.
    - **D-012**: Propose no-evidence = fewer than 3 results above score threshold 0.3 (tunable via config).
  - Implementation notes: Need `IRetrievalService` interface returning `RetrievalResult` (ranked chunks, scores, ACL-filtered count, no-evidence flag, trace ID). Phase 1 retrieves from Evidence Index only.

- [ ] P0-013: Implement chat orchestration with structured outputs.
  - Specs: jtbd-04
  - Dependencies: P0-012 (retrieval), **D-003 (confidence scoring)**, **D-006 (OpenAI model)**, **D-010 (session token budget)**, **D-013 (hallucination degradation)**
  - Exit criteria: response contract schema-validated; citations required for factual claims; low-confidence responses produce clarifying questions and diagnostic next steps; evidence-to-answer trace links persisted durably in SQL; hallucination-prone paths degrade to explicit "I don't know" with next-step guidance; session context bounded by configurable token budget.
  - Design decisions to resolve before implementation:
    - **D-003**: Propose 0-1 float from model self-report + heuristic blend (retrieval score average + evidence count). Categorical labels derived from thresholds: High (>=0.7), Medium (0.4-0.7), Low (<0.4).
    - **D-006**: Propose `gpt-4o` (latest) as default, configurable via `OpenAiSettings.Model`.
    - **D-010**: Propose sliding window with hard cutoff at 80% of model context window. Oldest messages dropped first. Summarization deferred to Phase 2.
    - **D-013**: Propose refuse + next-steps as default. When confidence < 0.3 and no evidence above threshold, respond with "I don't have enough information" + diagnostic next steps + optional escalation suggestion.
  - Implementation notes: Need `IChatOrchestrator` interface, `ChatRequest`/`ChatResponse` DTOs, `CitationDto` model. OpenAI structured output API for response schema enforcement. System prompt template with versioning. Token counting for context window management.

- [ ] P0-013A: Implement session and message persistence API for chat continuity.
  - Specs: jtbd-05
  - Dependencies: P0-013 (orchestration layer)
  - Exit criteria: sessions and messages stored in SQL with tenant scope; follow-up questions carry session context; session history retrievable for the owning user; session expiry/max-length policy configurable.
  - Implementation notes: `SessionEntity` and `MessageEntity` already exist in P0-005. Need CRUD endpoints: `POST /api/sessions`, `GET /api/sessions`, `GET /api/sessions/{id}/messages`, `POST /api/sessions/{id}/messages`. Need `CitationEntity` or JSON column on `MessageEntity` for citation persistence (jtbd-04 requires "persist trace links"). Session expiry: default 24h, configurable per tenant.

- [ ] P0-014: Enforce "never pass restricted content to model" check in orchestration path.
  - Specs: jtbd-03, jtbd-10
  - Dependencies: P0-012 (retrieval returns ACL metadata), P0-013 (orchestration assembles prompt)
  - Exit criteria: ACL trimming occurs before prompt assembly; restricted documents excluded from model context; integration test proves restricted content never reaches generation layer.

- [ ] P0-014A: Implement baseline PII detection and redaction in orchestration path.
  - Specs: jtbd-10
  - Dependencies: P0-013 (orchestration path exists)
  - Exit criteria: PII patterns (emails, phone numbers, SSNs, credit cards) detected and redacted/masked before model context assembly; redaction events written to immutable audit event store; redaction rules test-covered.
  - Implementation notes: Baseline scope — regex-based detection for common PII patterns. `PiiFlags` field on `CanonicalRecord` populated during enrichment (P0-010). Advanced policy controls deferred to P2-001. Tooling choice: start with custom regex; evaluate Azure AI Language PII or Presidio for Phase 2.

- [ ] P0-015: Implement escalation recommendation + structured handoff draft object.
  - Specs: jtbd-08
  - Dependencies: P0-013 (orchestration produces escalation signal), **D-004 (escalation policy schema)**
  - Exit criteria: response includes target team, reason, severity, suspected component, evidence links, and required handoff fields when escalation thresholds/policies are met; handoff draft reviewable by agent before any external action; all required structured fields validated as present when determinable.
  - Design decision to resolve:
    - **D-004**: Propose confidence-based trigger: escalation recommended when confidence < 0.4 AND severity >= P2. Per-tenant team routing table in SQL (tenant_id, product_area, target_team, escalation_threshold). Global fallback to "Engineering" team.
  - Implementation notes: `POST /api/escalations/draft` endpoint. `EscalationDraft` DTO with title, customer_summary, steps_to_reproduce, logs_ids_requested, suspected_component, severity, evidence_links, target_team, reason. Phase 1: copy/export only (R-011).

### P0 Frontend MVP

- [ ] P0-016: Implement React agent chat with session continuity, confidence badge, citations, and Evidence Drawer.
  - Specs: jtbd-05
  - Dependencies: P0-013A (session API), P0-013 (chat API)
  - Exit criteria: user can ask questions and follow up within a session; Evidence Drawer shows snippet, source location, timestamp, and access label per citation; confidence badge displayed on each response.
  - Implementation notes: Need MSAL React integration for Entra auth. Chat thread component, message input, Evidence Drawer panel. Confidence badge rendering depends on D-003 resolution (propose colored label: green/yellow/red). Streaming response support not in spec but should add typing indicator for P95 <= 8s SLO UX.

- [ ] P0-017: Implement next-steps and escalation UX (CTA + handoff draft review/edit + copy/export).
  - Specs: jtbd-04, jtbd-05, jtbd-08
  - Dependencies: P0-015 (escalation draft API), P0-016 (chat UI)
  - Exit criteria: escalation CTA visible when escalation signal is present; handoff draft can be reviewed, edited, and copied/exported by agent; next-step guidance displayed for low-confidence responses.
  - Implementation notes: Phase 1 provides copy-to-clipboard and markdown export. ADO/ClickUp buttons disabled with "Coming soon" tooltip (R-011).

- [ ] P0-018: Implement feedback capture UI + API wiring.
  - Specs: jtbd-05, jtbd-06
  - Dependencies: P0-016 (chat UI), P0-013A (session/message API)
  - Exit criteria: feedback events persist with trace ID and session linkage; reason codes selectable from predefined list; correction text and corrected-answer proposals stored when provided.
  - Implementation notes: Need `POST /api/sessions/{id}/messages/{id}/feedback` endpoint. Reason codes must be defined — propose initial set: `wrong_answer`, `outdated_info`, `missing_context`, `wrong_source`, `too_vague`, `other`. `FeedbackEntity` exists in P0-005.

- [ ] P0-018A: Implement outcome tracking API and UI.
  - Specs: jtbd-06, jtbd-08
  - Dependencies: P0-018 (feedback API)
  - Exit criteria: outcome events stored in SQL per session with tenant scope; outcomes queryable for reporting; linked to escalation trace and session ID; routing quality metrics computable.
  - Implementation notes: `OutcomeEventEntity` exists in P0-005. Need `POST /api/sessions/{id}/outcome` endpoint. `ResolutionType` enum: ResolvedWithoutEscalation, Escalated, Rerouted (3 values — jtbd-08 adds Rerouted beyond jtbd-06's 2).

- [ ] P0-019: Implement admin connectors dashboard baseline.
  - Specs: jtbd-05, jtbd-07
  - Dependencies: P0-006 (connector admin API complete), P0-016 (React app base)
  - Exit criteria: admin can onboard a connector, test connection, and run sync without redeploy; sync run status and recent errors visible; field mapping UI; test-connection pass/fail; UI routes enforce role checks client-side.
  - Implementation notes: Wizard flow: choose type -> auth -> scope -> mapping -> test -> activate. ScheduleCron stored but no scheduler — display as informational only in Phase 1.

### P0 Evaluation, SLOs, and Observability MVP

- [ ] P0-020: Instrument OpenTelemetry + correlation IDs across all paths; wire audit event writes.
  - Specs: jtbd-06, jtbd-10
  - Dependencies: P0-013 (orchestration path exists)
  - Exit criteria: traces/logs correlate end-to-end; correlation IDs propagated through Service Bus; Application Insights receives structured telemetry; immutable audit events for queries, retrieval, answers, escalations, admin changes, cross-tenant denials, PII redaction.
  - Implementation notes: Baseline Activity tagging exists (P0-003). Need OpenTelemetry SDK for ASP.NET Core, HttpClient, EF Core, Azure SDK. Application Insights exporter.

- [ ] P0-020A: Implement audit log query and export API.
  - Specs: jtbd-10
  - Dependencies: P0-020, **D-011 (audit export format)**
  - Exit criteria: audit events queryable by tenant, date range, event type, actor; export in structured format; role-gated to `SecurityAuditor` and `Admin`.
  - Design decision to resolve:
    - **D-011**: Propose NDJSON with cursor-based pagination. Export via `GET /api/audit/events/export` returning NDJSON stream.
  - Implementation notes: Current `GET /api/audit/events` is a placeholder stub — replace with real query implementation. `audit:export` permission exists but has no backing endpoint.

- [ ] P0-021: Implement baseline evaluation harness.
  - Specs: jtbd-06
  - Dependencies: P0-013 (chat orchestration), **D-007 (gold dataset strategy)**
  - Exit criteria: gold dataset schema defined; eval job produces retrieval precision, groundedness, citation coverage, routing accuracy, no-evidence rate; results compared against baseline; regression alerts; release gating.
  - Design decision to resolve:
    - **D-007**: Propose JSONL format, 30-50 manually authored cases, PR-based review, min 30 cases before gated release. Stored in `eval/gold-dataset/`.
  - Implementation notes: Spec has no numeric SLO thresholds. Propose: groundedness >= 0.80, citation coverage >= 0.70, routing accuracy >= 0.60, no-evidence rate <= 0.25. Flag on regression > 2%, block on regression > 5%.

- [ ] P0-022: Implement SLO dashboards and alerts.
  - Specs: jtbd-06, jtbd-10
  - Dependencies: P0-020
  - Exit criteria: dashboards and alert thresholds active in non-dev env; ingestion lag and dead-letter rate visible.
  - Implementation notes: P95 answer-ready <= 8s, availability >= 99.5%. Need Azure Monitor alert rules in IaC. Propose P95 sync lag <= 15 minutes as operational target.

## Phase 2 (V1) Priority Queue

- [ ] P1-001: Add HubSpot connector.
  - Specs: jtbd-01, jtbd-07
  - Exit criteria: HubSpot tickets ingested with ACL metadata and tenant-scoped checkpoints; webhook signature validation; backfill 3k+; polling fallback.

- [ ] P1-002: Add ClickUp connector.
  - Specs: jtbd-01, jtbd-07
  - Exit criteria: ClickUp docs/tasks ingested with ACL metadata; HMAC signature verification; backfill 3k+; polling fallback.

- [ ] P1-003: Implement external draft escalation creation in ADO/ClickUp after human approval.
  - Specs: jtbd-08

- [ ] P1-004: Implement Case-Pattern Store index and retrieval fusion with Evidence Store.
  - Specs: jtbd-02, jtbd-03, jtbd-09
  - Implementation notes: Phase 1 retrieval (P0-012) uses Evidence Index only. This adds Pattern Index and cross-index merge with trust_level/recency/authority boosts and diversity constraint.

- [ ] P1-005: Implement solved-ticket pattern distillation pipeline.
  - Specs: jtbd-02, jtbd-09
  - Dependencies: **D-008 (solved-ticket criteria)**
  - Design decision to resolve:
    - **D-008**: Propose: status in (Closed, Resolved) AND resolution_type = ResolvedWithoutEscalation AND positive_feedback >= 1. Configurable per tenant.

- [ ] P1-006: Implement pattern governance workflows.
  - Specs: jtbd-09, jtbd-10
  - Implementation notes: Trust states: `draft`, `reviewed`, `approved`, `deprecated` (4-state model per PRD). `pattern:approve` and `pattern:deprecate` permissions already in RolePermissions. Need CasePatternEntity, governance endpoints, governance queue UI.

- [ ] P1-007: Add advanced retrieval filters and tuning controls.
  - Specs: jtbd-03, jtbd-07

- [ ] P1-008: Expand admin diagnostics.
  - Specs: jtbd-07
  - Implementation notes: Dead-letter peek service and endpoint exist. This adds UI and remaining diagnostics (webhook status, rate-limit alerts).

- [ ] P1-009: Add outcome-driven routing improvement loop.
  - Specs: jtbd-06, jtbd-08
  - Dependencies: P0-018A

- [ ] P1-010: Implement IaC drift checks.
  - Specs: jtbd-11

- [ ] P1-011: Implement richer enrichment + case-card quality hardening.
  - Specs: jtbd-02

- [ ] P1-012: Implement Terraform remote state backend.
  - Specs: jtbd-11
  - Dependencies: **D-009**
  - Design decision: **D-009**: Propose Azure Storage backend with blob lease locking.

## Phase 3+ (V2+) Priority Queue

- [ ] P2-001: Stricter privacy tooling (PII policy controls, redaction audit, retention, right-to-delete propagation).
  - Specs: jtbd-10

- [ ] P2-002: Policy-aware team playbooks and configurable routing policies per tenant.
  - Specs: jtbd-08, jtbd-10

- [ ] P2-003: Cost optimization controls (token budgets, retrieval compression, embedding cache lifecycle).
  - Specs: jtbd-06

- [ ] P2-004: Pattern maintenance automation and contradiction detection with human review gates.
  - Specs: jtbd-09

- [ ] P2-005: Configurable data retention policies with measurable execution.
  - Specs: jtbd-10

## Cross-Cutting Test Backlog (continuous)
- [ ] T-001: Unit tests for normalization/chunking, structured output parsing, ACL and tenant filters, escalation policy logic, PII redaction rules.
- [ ] T-002: Integration tests for connector contracts (all auth types), webhook signature verification, search indexing/retrieval, OpenAI error handling/retries, Key Vault resolution.
- [ ] T-003: E2E tests for agent journey (answer+citation+feedback+outcome) and admin journey (connect->map->test->sync->validate->query).
- [ ] T-004: Security tests for RBAC, cross-tenant leakage, restricted-content exclusion, redaction behavior, audit completeness.
- [ ] T-005: Load tests for concurrent chat, ingestion bursts, webhook spikes, search index throughput.
- [ ] T-006: IaC tests/validations for Terraform and ARM on every infra change.

## Open Risks / Watch Items
- [ ] R-001: Connector API limits and webhook reliability variability across providers.
- [ ] R-002: Retrieval quality drift as corpus grows; requires active eval + tuning.
- [ ] R-003: Escalation over/under-triggering before enough outcome data accumulates.
- [ ] R-004: Tenant misconfiguration risk in early environments.
- [ ] R-005: Terraform/ARM drift risk if infra updates bypass IaC workflows.
- [ ] R-006: PII leakage into model context before redaction rules are mature; baseline detection in Phase 1 mitigates but does not eliminate.
- [ ] R-007: Service Bus message ordering under high load; design for idempotency.
- [x] R-008: Embedding model and chunking parameters — resolved in P0-005C.
- [ ] R-009: Dual Terraform + ARM maintenance burden. ~~Active divergence found: ARM missing SQL Entra admin (BUG-002)~~ — fixed.
- [ ] R-010: Confidence/escalation thresholds undefined; treat as tunable config informed by eval runs.
- [ ] R-011: Phase 1 escalation drafts — copy/export only. Must communicate in UX.
- [x] R-012: ACL metadata models differ per source. Each connector must document its ACL mapping. ADO: area paths → Restricted groups. SharePoint: drive name → Restricted groups.
- [ ] R-013: Session context may exceed token limits in long conversations.
- [x] R-014: ~~No Ingestion Worker resource in IaC~~ — resolved (TECH-001 complete).
- [x] R-015: ~~Service Bus uses connection string not Managed Identity~~ — resolved (TECH-002 complete).
- [ ] R-016: Feedback reason codes not enumerated in any spec — must define before P0-018.
- [ ] R-017: jtbd-06 has no numeric SLO thresholds — eval harness (P0-021) cannot gate without agreed values.
- [ ] R-018: Entra ID config optional with silent fallback — misconfiguration risk in production.
- [ ] R-019: jtbd-03 spec very thin (33 lines) — all detail in PRD. Risk of divergence.
- [x] R-020: ~~No .NET or frontend CI pipeline~~ — resolved (TECH-006 complete; `ci.yml` added).

## Open Design Decisions (must resolve before dependent items)

- [x] D-001: Embedding model — resolved: text-embedding-3-large at 1536 dimensions.
- [x] D-002: Chunking strategy — resolved: 512 tokens/chunk, 64 overlap, structural boundaries.
- [ ] D-003: Confidence scoring methodology — blocks P0-013.
  - **Proposed**: 0-1 float, model self-report + heuristic blend. Categorical: High (>=0.7), Medium (0.4-0.7), Low (<0.4).
- [ ] D-004: Escalation policy schema and thresholds — blocks P0-015.
  - **Proposed**: Confidence < 0.4 AND severity >= P2. Per-tenant routing table in SQL. Fallback to "Engineering".
- [ ] D-005: Top-k and RRF fusion weights — blocks P0-012.
  - **Proposed**: top-k=20, equal RRF weights (1.0/1.0). Semantic reranker on merged top-20. Pattern Index deferred to P1-004.
- [ ] D-006: OpenAI model version — blocks P0-013.
  - **Proposed**: `gpt-4o` (latest), configurable via `OpenAiSettings.Model`.
- [ ] D-007: Gold dataset strategy — blocks P0-021.
  - **Proposed**: JSONL, 30-50 manual cases, PR review, min 30 for gated release. In `eval/gold-dataset/`.
- [ ] D-008: Solved-ticket candidate criteria — blocks P1-005.
  - **Proposed**: Status in (Closed, Resolved) AND ResolvedWithoutEscalation AND positive_feedback >= 1.
- [ ] D-009: Terraform remote state backend — blocks P1-012.
  - **Proposed**: Azure Storage with blob lease locking.
- [ ] D-010: Session token budget — blocks P0-013, P0-013A.
  - **Proposed**: Sliding window, hard cutoff at 80% context window, drop oldest first. Summarization in Phase 2.
- [ ] D-011: Audit export format — blocks P0-020A.
  - **Proposed**: NDJSON with cursor-based pagination.
- [ ] D-012: No-evidence threshold — blocks P0-012.
  - **Proposed**: < 3 results above score 0.3 (both conditions, tunable).
- [ ] D-013: Hallucination degradation — blocks P0-013.
  - **Proposed**: Refuse + next-steps. Confidence < 0.3 + no evidence -> "I don't have enough information" + diagnostic steps + optional escalation.

## Spec Clarification Backlog
Items where specs are ambiguous, inconsistent, or missing detail. Patch before or during dependent implementation.

- [x] SPEC-001: jtbd-01 — Add webhook registration lifecycle (register/renew/deregister on enable/disable). Resolved: P0-008A implements register on enable, deregister on disable via `IWebhookManager`. Renewal deferred (ADO hooks don't expire).
- [x] SPEC-002: jtbd-01 — Define polling fallback interval and failure detection mechanism. Resolved: P0-008A implements 5-minute default interval with 0-60s jitter, 3 consecutive failure threshold, `WebhookPollingFallbackService` BackgroundService.
- [ ] SPEC-003: jtbd-01 — Define content-level dedup strategy using ContentHash.
- [ ] SPEC-004: jtbd-02 — Define enrichment version scheme (format, storage, reprocessing trigger).
- [x] SPEC-005: jtbd-02 — Define error token extraction method; add `ErrorTokens` field to CanonicalRecord. Resolved: `ErrorTokens` added to `CanonicalRecord`. `BaselineEnrichmentService.ExtractErrorTokens` uses regex for exception names, HTTP status codes, error codes (ERR-xxx, AADSTS), and hex codes (0x...).
- [x] SPEC-006: jtbd-02 — Standardize chunk ID format (`_` in code vs `#` in PRD). Resolved: underscore format `{EvidenceId}_chunk_{index}` is canonical. PRD `#` format deprecated.
- [ ] SPEC-007: jtbd-03 — Expand thin spec (33 lines) with PRD detail (query stages, field schema, merge algorithm, telemetry).
- [ ] SPEC-008: jtbd-06 — Enumerate feedback reason codes.
- [ ] SPEC-009: jtbd-06 — Define numeric SLO thresholds for eval gates.
- [ ] SPEC-010: jtbd-08 — Define routing rule precedence for multiple matching rules.
- [ ] SPEC-011: jtbd-08 — Define severity classification ownership (LLM, classification, agent, source ticket).
- [ ] SPEC-012: jtbd-09 — Resolve trust state mismatch: spec 3 states vs PRD 4 states. Propose 4.
- [ ] SPEC-013: jtbd-09 — Define pattern usage/reuse metrics schema.
- [ ] SPEC-014: jtbd-10 — Specify PII detection tooling and category list.
- [ ] SPEC-015: jtbd-10 — Define default retention window values and entity-to-window mapping.
- [ ] SPEC-016: jtbd-10 — Define cross-tenant access detection beyond missing-tid.
- [x] SPEC-017: jtbd-11 — Add Ingestion Worker to IaC resource inventory. Resolved: TECH-001 complete.

## Plan Maintenance Checklist (run each iteration)
- [ ] Mark completed items with evidence (PR/test IDs).
- [ ] Reorder remaining items by risk and dependency.
- [ ] Add newly discovered bugs/tech debt with priority.
- [ ] Confirm Terraform and ARM templates remain synchronized after Azure resource changes.
- [ ] Remove stale completed details to keep plan concise.
