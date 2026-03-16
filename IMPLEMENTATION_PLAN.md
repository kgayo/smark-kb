# IMPLEMENTATION_PLAN

Last updated: 2026-03-16 (Asia/Manila) — iteration 33 (P0-016 complete)
Status: Active backlog (P0-001 through P0-016 complete; 0 bugs blocking, 0 tech-debt blocking; next up P0-017)

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

- [x] P0-010A: Set up Azure Blob Storage raw content store for ingested snapshots and extracted text.
  - Specs: jtbd-01, jtbd-02
  - Dependencies: P0-005A (Blob resource provisioned in IaC)
  - Completed: `IBlobStorageService` interface + `AzureBlobStorageService` implementation in `SmartKb.Contracts.Services`. `BlobStorageSettings` config class (ServiceUri for Managed Identity, ConnectionString fallback). Tenant-scoped blob path convention `{tenantId}/{connectorType}/{evidenceId}/raw`. `RawContentSnapshotEntity` in SQL with `AddRawContentSnapshots` migration (PK=EvidenceId, connector FK, content hash for dedup, content length, content type). `SyncJobProcessor` uploads raw content to blob before normalization; skips upload when content hash unchanged (dedup). `Azure.Storage.Blobs` package added to Contracts. `BlobContainerClient` registered in both API and Ingestion DI with Managed Identity preference. `Storage Blob Data Contributor` role assigned to both API and Ingestion App Services in Terraform (`storage.tf`) and ARM (`main.json`). `BlobStorage__ServiceUri` app setting added to both App Services. ARM template validated (22 resources, 13 outputs). 22 new tests (8 blob settings + path convention, 8 blob storage service, 4 SyncJobProcessor blob upload, 2 existing test updates); all 432 tests passing.

- [x] P0-011: Implement Azure AI Search Evidence index and indexing pipeline.
  - Specs: jtbd-03
  - Dependencies: P0-010 (chunks must exist to index)
  - Completed: `IIndexingService` interface + `AzureSearchIndexingService` implementation in `SmartKb.Contracts.Services`. Index schema built from `SearchFieldNames` with all 17 fields: key (`chunk_id`), 3 searchable text fields (`chunk_text`, `chunk_context`, `title` with EnMicrosoft analyzer), vector field (`embedding_vector` at 1536 dims), 8 filterable metadata fields (tenant_id, evidence_id, source_system, source_type, status, updated_at, product_area, tags), 3 ACL security trimming fields (visibility, allowed_groups, access_label), source_url. Vector search: HNSW algorithm with cosine metric (M=4, efConstruction=400, efSearch=500). Semantic search: `evidence-semantic-config` with title as TitleField, chunk_text+chunk_context as ContentFields, tags as KeywordsFields. Index creation is idempotent (CreateOrUpdate). Batch indexing with configurable batch size (default 100). `SearchServiceSettings` config class (Endpoint for Managed Identity, AdminApiKey fallback). `SyncJobProcessor` calls `IndexChunksAsync` after chunk persistence — indexing failures are non-fatal (chunks persisted in SQL for retry). `SearchIndexClient` registered in both API and Ingestion DI with Managed Identity preference. `SearchService__Endpoint` app setting added to both App Services in Terraform and ARM. `Search Index Data Contributor` + `Search Service Contributor` RBAC roles assigned to both API and Ingestion App Services in Terraform (`search.tf`) and ARM (`main.json`). ARM template validated (26 resources, 14 outputs). 27 new tests (6 settings, 17 index schema/document mapping, 4 SyncJobProcessor indexing integration); all 459 tests passing.

### P0 Retrieval + Orchestration MVP

- [x] P0-012: Implement hybrid retrieval service (vector + BM25 + semantic rerank).
  - Specs: jtbd-03
  - Dependencies: P0-011 (complete), D-005 (resolved), D-012 (resolved)
  - Completed: `IRetrievalService` interface + `AzureSearchRetrievalService` implementation in `SmartKb.Contracts.Services`. Hybrid search: vector (VectorizedQuery on `embedding_vector` field, HNSW cosine) + BM25 (text search on `chunk_text`, `chunk_context`, `title`). Azure AI Search handles RRF score fusion natively. Optional semantic reranking via `evidence-semantic-config` with extractive captions. Tenant isolation via OData filter on `tenant_id` (always server-side). ACL security trimming in-memory post-retrieval: Public/Internal pass through, Restricted requires user membership in `allowed_groups` (case-insensitive). `RetrievalSettings` config class with D-005 defaults (TopK=20, RrfK=60, equal BM25/vector weights 1.0/1.0) and D-012 defaults (NoEvidenceScoreThreshold=0.3, NoEvidenceMinResults=3). `RetrievalResult` DTO returns ranked `RetrievedChunk` list, `AclFilteredOutCount`, `HasEvidence` flag, and `TraceId` for audit. `RetrievedChunk` DTO includes citation metadata (title, source URL, source system/type, updated_at, access label, tags) plus RRF and optional semantic scores. Retrieval telemetry logs: total raw results, ACL-filtered count, returned count, has-evidence flag, above-threshold count, duration, top-5 chunk IDs and scores, trace ID. Over-fetches 2x TopK to account for ACL filtering. Error handling: `RequestFailedException` returns empty result with `HasEvidence=false`. OData value escaping for tenant ID. Services registered in both API and Ingestion DI. Phase 1 retrieves from Evidence Index only; Pattern Index fusion deferred to P1-004. 24 new tests (7 settings, 9 ACL filtering, 4 no-evidence detection, 2 OData escaping, 2 DTO validation); all 483 tests passing.

- [x] P0-013: Implement chat orchestration with structured outputs.
  - Specs: jtbd-04
  - Dependencies: P0-012 (complete), D-003 (resolved), D-006 (resolved), D-010 (resolved), D-013 (resolved)
  - Completed: `IChatOrchestrator` interface + `ChatOrchestrator` implementation in `SmartKb.Contracts.Services`. Full orchestration pipeline: embed query → hybrid retrieve → ACL-filtered evidence → assemble grounded prompt → OpenAI structured output (`json_schema` strict mode, `grounded_answer` schema) → blend confidence → persist trace → return response. `IEmbeddingService` + `OpenAiEmbeddingService` for query embedding via OpenAI Embeddings API (text-embedding-3-large@1536). `ChatOrchestrationSettings` config with D-003 thresholds (High>=0.7, Medium>=0.4, Low<0.4), D-010 token budget (102k = 80% of gpt-4o 128k context), D-013 degradation threshold (0.3). Structured response contract: `ChatResponse` with `ResponseType` (final_answer/next_steps_only/escalate), `Answer`, `Citations` (mapped `CitationDto` with snippet + source metadata), blended `Confidence` + `ConfidenceLabel`, `NextSteps`, `EscalationSignal`, `TraceId`, `HasEvidence`, `SystemPromptVersion`. D-003 confidence scoring: blended = 0.6×modelSelfReport + 0.4×retrievalHeuristic (avgRrfScore × saturation factor). D-010 sliding window: oldest session messages dropped first when budget exceeded. D-013 degradation: when blendedConfidence < 0.3 and response_type was final_answer, overrides to next_steps_only with explicit "I don't have enough information" + diagnostic steps + escalation signal. No-evidence path returns next_steps_only with escalation recommended to Engineering. `AnswerTraceEntity` in SQL with `AddAnswerTraces` migration for durable evidence-to-answer trace links (tenant-scoped, correlation ID indexed). `IAnswerTraceWriter` + `SqlAnswerTraceWriter`. `POST /api/chat` endpoint with `chat:query` permission (SupportAgent, SupportLead, Admin). Conditional DI registration (requires OpenAI API key + Search Service configured). 503 graceful degradation when orchestrator unavailable. Registered in API DI. 49 new tests (12 settings, 21 orchestrator unit, 5 embedding DTO, 5 API endpoint integration, 6 chat DTO validation); all 532 tests passing.

- [x] P0-013A: Implement session and message persistence API for chat continuity.
  - Specs: jtbd-05
  - Dependencies: P0-013 (complete)
  - Completed: `SessionSettings` config class (default 24h expiry, max 200 messages/session, max 50 active sessions/user — all configurable via `Session:*` app settings). `SessionEntity` extended with `Title`, `CustomerRef`, `ExpiresAt` fields. `MessageEntity` extended with `CitationsJson` (JSON column for citation persistence), `Confidence`, `ConfidenceLabel`, `ResponseType` fields. `AddSessionPersistence` migration. `ISessionService` interface + `SessionService` implementation in `SmartKb.Data.Repositories`. Session CRUD: `POST /api/sessions` (create), `GET /api/sessions` (list user's sessions), `GET /api/sessions/{id}` (get detail), `DELETE /api/sessions/{id}` (soft-delete). Message endpoints: `GET /api/sessions/{id}/messages` (chronological history with persisted citations), `POST /api/sessions/{id}/messages` (orchestrate chat + persist user and assistant messages). Follow-up messages carry full session history to `ChatOrchestrator` for multi-turn context (D-010 token budget applies). Auto-title sessions from first user query when no title set. Session expiry extended on each message activity. Expired/over-limit sessions return 404. Tenant isolation + user ownership enforced on all operations. All endpoints require `chat:query` permission (SupportAgent, SupportLead, Admin). Stateless `POST /api/chat` endpoint preserved for backward compatibility. Registered in API DI via `DataServiceExtensions`. 35 new tests (4 settings, 14 service unit, 17 endpoint integration).

- [x] P0-014: Enforce "never pass restricted content to model" check in orchestration path.
  - Specs: jtbd-03, jtbd-10
  - Dependencies: P0-012 (complete), P0-013 (complete)
  - Completed: Defense-in-depth ACL enforcement in `ChatOrchestrator.EnforceRestrictedContentExclusion` — second ACL check between retrieval and prompt assembly. `RetrievedChunk` extended with `Visibility` and `AllowedGroups` fields for orchestration-layer verification. `TenantContext` extended with `UserGroups` (backward-compatible). `TenantContextMiddleware` extracts `groups` and `roles` claims from Entra ID JWT and populates `UserGroups`. API endpoints (`/api/chat`, `/api/sessions/{id}/messages`) inject JWT-extracted groups into requests (server-side groups take precedence over client-provided). Critical security log emitted if restricted content bypasses retrieval layer. Integration test proves restricted content never reaches `BuildSystemPrompt`. 13 new tests (10 restricted content exclusion unit + 3 middleware group extraction); all 580 tests passing.

- [x] P0-014A: Implement baseline PII detection and redaction in orchestration path.
  - Specs: jtbd-10
  - Dependencies: P0-013 (complete)
  - Completed: `IPiiRedactionService` interface + `PiiRedactionService` implementation in `SmartKb.Contracts.Services`. Regex-based redaction of 4 PII categories: emails → `[REDACTED-EMAIL]`, phone numbers → `[REDACTED-PHONE]`, SSNs → `[REDACTED-SSN]`, credit card numbers → `[REDACTED-CREDIT-CARD]`. Same regex patterns as `BaselineEnrichmentService.DetectPii` (kept in sync). `ChatOrchestrator.RedactPiiInChunks` static method applies redaction to both `ChunkText` and `ChunkContext` of retrieved chunks. Defense-in-depth: runs after ACL enforcement (step 3.5) and before prompt assembly (step 4), ensuring no PII reaches model context regardless of indexing pipeline behavior. Audit events: `pii.redaction` event written to immutable audit store via `IAuditEventWriter` when PII is redacted, with tenant/user/correlation context. `PiiRedactedCount` field added to `ChatResponse` for client transparency. `ChatOrchestrator` constructor now accepts `IPiiRedactionService` and `IAuditEventWriter` dependencies. `PiiRedactionService` registered as singleton in API DI. 17 new tests (10 PII redaction service unit + 7 RedactPiiInChunks integration including end-to-end proof that PII never reaches `BuildSystemPrompt`); all 597 tests passing. Advanced policy controls deferred to P2-001.

- [x] P0-015: Implement escalation recommendation + structured handoff draft object.
  - Specs: jtbd-08
  - Dependencies: P0-013 (complete), D-004 (resolved)
  - Completed: `EscalationDraftEntity` in SQL with `AddEscalationDrafts` migration (soft-delete, tenant-scoped, session FK, message reference). `EscalationRoutingRuleEntity` for per-tenant team routing (product area → target team, configurable threshold + min severity, unique active rule per product area per tenant). `EscalationSettings` config class with D-004 defaults (threshold=0.4, minSeverity=P2, fallback team="Engineering"). Severity ordering (P1>P2>P3>P4) with `MeetsSeverityThreshold` helper. `IEscalationDraftService` interface + `EscalationDraftService` implementation in `SmartKb.Data.Repositories`. Full CRUD: `POST /api/escalations/draft` (create draft from session message), `GET /api/escalations/draft/{draftId}` (get for review), `GET /api/sessions/{sessionId}/escalations/drafts` (list drafts for session), `PUT /api/escalations/draft/{draftId}` (update/edit before export), `GET /api/escalations/draft/{draftId}/export` (export as markdown), `DELETE /api/escalations/draft/{draftId}` (soft-delete). All endpoints require `chat:query` permission (SupportAgent, SupportLead, Admin). Structured handoff fields: title, customer_summary, steps_to_reproduce, logs_ids_requested, suspected_component, severity, evidence_links (JSON citations), target_team, reason. Routing rule lookup: when target team not specified, resolves from per-tenant `EscalationRoutingRules` table by suspected component; falls back to "Engineering". Audit event written on draft creation (`escalation.draft.created`). Markdown export includes all structured fields with evidence links. `ExportedAt` timestamp tracked. Tenant isolation + user ownership enforced on all operations. Phase 1: copy/export only (R-011); external ticket creation deferred to P1-003. Registered in API DI via `DataServiceExtensions`. 43 new tests (22 service unit + 4 settings + 17 endpoint integration); all 644 tests passing.

### P0 Frontend MVP

- [x] P0-016: Implement React agent chat with session continuity, confidence badge, citations, and Evidence Drawer.
  - Specs: jtbd-05
  - Dependencies: P0-013A (session API), P0-013 (complete)
  - Completed: Full React chat UI with MSAL Entra ID authentication (conditional — bypassed in local dev when `VITE_ENTRA_CLIENT_ID` not set). `AuthProvider` wraps app with `MsalProvider` + `AuthGate` (login redirect flow, silent token acquisition with `InteractionRequiredAuthError` fallback). API client layer (`api/client.ts`) with bearer token injection, `ApiResponse<T>` unwrapping, typed fetch for all session and chat endpoints. `SessionSidebar` component: session list with title, message count, time-ago display, create/select/delete actions, active highlight. `ChatThread` component: user and assistant message rendering, `ConfidenceBadge` (green High >=0.7, yellow Medium 0.4-0.7, red Low <0.4 per D-003), inline citation count button, next-steps list, escalation banner (purple, target team + reason). `EvidenceDrawer` slide-out panel: citation cards with title, snippet (4-line clamp), source system, formatted date, access label (colored: green Public, yellow Internal, red Restricted), external source link. `MessageInput` with Enter-to-send (Shift+Enter for newline), disabled state with "Thinking..." indicator. Typing dots animation during API call. `ChatPage` orchestrates: session CRUD via API, message send with response meta (nextSteps, escalation) stored in `metaMap`, auto-scroll, error banner, drawer state. `index.css` with CSS variables, responsive layout (sidebar 260px + main + drawer 340px). TypeScript strict mode, Vite env types for MSAL config. ESLint config (`.eslintrc.cjs`) with TS parser. 44 new frontend tests (3 App, 5 ConfidenceBadge, 9 EvidenceDrawer, 9 MessageInput, 9 ChatThread, 7 SessionSidebar, 2 API client); all passing. Build clean (191 modules, 7.3KB CSS + 440KB JS gzipped). All 644 backend tests still passing.

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
  - Dependencies: P0-013 (complete)
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
  - Dependencies: P0-013 (complete), **D-007 (gold dataset strategy)**
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
- [x] R-013: ~~Session context may exceed token limits~~ — resolved (D-010: sliding window with 80% context window budget, oldest messages dropped first).
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
- [x] D-003: Confidence scoring methodology — resolved: 0-1 float, blended = 0.6×modelSelfReport + 0.4×retrievalHeuristic (avgRrfScore × saturation). Categorical: High (>=0.7), Medium (0.4-0.7), Low (<0.4). Configurable via `ChatOrchestrationSettings`.
- [x] D-004: Escalation policy schema and thresholds — resolved: confidence < 0.4 AND severity >= P2. Per-tenant `EscalationRoutingRules` table (tenant_id, product_area, target_team, escalation_threshold, min_severity). Fallback to "Engineering". Configurable via `EscalationSettings`.
- [x] D-005: Top-k and RRF fusion weights — resolved: top-k=20, equal RRF weights (1.0/1.0), semantic reranker on merged top-20. Pattern Index deferred to P1-004. Configurable via `RetrievalSettings`.
- [x] D-006: OpenAI model version — resolved: `gpt-4o` (latest), configurable via `OpenAiSettings.Model`. Temperature 0.2 for grounded generation.
- [ ] D-007: Gold dataset strategy — blocks P0-021.
  - **Proposed**: JSONL, 30-50 manual cases, PR review, min 30 for gated release. In `eval/gold-dataset/`.
- [ ] D-008: Solved-ticket candidate criteria — blocks P1-005.
  - **Proposed**: Status in (Closed, Resolved) AND ResolvedWithoutEscalation AND positive_feedback >= 1.
- [ ] D-009: Terraform remote state backend — blocks P1-012.
  - **Proposed**: Azure Storage with blob lease locking.
- [x] D-010: Session token budget — resolved: sliding window, hard cutoff at 80% of gpt-4o 128k (102,400 tokens). Oldest messages dropped first. Configurable via `ChatOrchestrationSettings.MaxTokenBudget`. Summarization deferred to Phase 2.
- [ ] D-011: Audit export format — blocks P0-020A.
  - **Proposed**: NDJSON with cursor-based pagination.
- [x] D-012: No-evidence threshold — resolved: < 3 results above score 0.3 (both conditions, tunable via `RetrievalSettings.NoEvidenceScoreThreshold` and `NoEvidenceMinResults`).
- [x] D-013: Hallucination degradation — resolved: refuse + next-steps. Blended confidence < 0.3 overrides final_answer to next_steps_only with "I don't have enough information" + diagnostic steps + escalation to Engineering. No-evidence path (HasEvidence=false) returns next_steps_only immediately. Configurable via `ChatOrchestrationSettings.DegradationThreshold`.

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
- [x] SPEC-014: jtbd-10 — Specify PII detection tooling and category list. Resolved: Phase 1 uses custom regex for 4 categories: email, phone, ssn, credit_card. Azure AI Language PII / Presidio evaluation deferred to Phase 2 (P2-001).
- [ ] SPEC-015: jtbd-10 — Define default retention window values and entity-to-window mapping.
- [ ] SPEC-016: jtbd-10 — Define cross-tenant access detection beyond missing-tid.
- [x] SPEC-017: jtbd-11 — Add Ingestion Worker to IaC resource inventory. Resolved: TECH-001 complete.

## Plan Maintenance Checklist (run each iteration)
- [ ] Mark completed items with evidence (PR/test IDs).
- [ ] Reorder remaining items by risk and dependency.
- [ ] Add newly discovered bugs/tech debt with priority.
- [ ] Confirm Terraform and ARM templates remain synchronized after Azure resource changes.
- [ ] Remove stale completed details to keep plan concise.
