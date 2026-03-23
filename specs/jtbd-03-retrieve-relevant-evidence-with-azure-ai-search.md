# JTBD 03: Retrieve Relevant Evidence with Hybrid Search and Security Trimming

## Job to Be Done
As a support agent, I need relevant and authorized evidence quickly so I can answer accurately without exposing restricted data.

## Scope
- Azure AI Search hybrid retrieval.
- RRF-style ranking (vector + lexical) with semantic reranking.
- Metadata and ACL filtering.
- Retrieval telemetry for debugging and eval.

## Requirements
- Use hybrid retrieval over vector + BM25/keyword candidates.
- Apply semantic reranking for top candidate refinement.
- Enforce ACL and tenant filters before returning evidence.
- Return ranked evidence with source links, snippets, and trace IDs.
- Emit retrieval telemetry: top IDs, scores, ACL filtered counts, no-evidence indicators.

## Retrieval Pipeline Stages

### Stage 1 — Pre-Retrieval Query Classification
- `gpt-4o-mini` classifies the user query into structured JSON: category, module, severity estimate, and filter recommendations.
- Classification biases downstream retrieval filters (product area, source type).

### Stage 2 — Hybrid Retrieval (Two-Index Strategy)
- **Evidence Index**: chunked source content (wiki, tickets, docs). Fields: `chunk_text`, `chunk_context`, `title` (searchable, EnMicrosoft analyzer); `embedding_vector` (1536-dim HNSW cosine); 8 filterable metadata fields; 3 ACL fields.
- **Pattern Index**: distilled case patterns with `problem_statement`, `resolution`, `root_cause`, `symptoms`, `workaround`, `verification_steps`, `escalation_playbook`, `applicability_constraints` (searchable); plus trust/tenant/product fields.
- Hybrid query: vector search (VectorizedQuery on `embedding_vector`) + BM25 text search on searchable fields.
- Azure AI Search applies RRF (Reciprocal Rank Fusion) to merge vector and BM25 result sets.
- Optional semantic reranking via `evidence-semantic-config` with extractive captions.
- Over-fetch 2× TopK to account for ACL filtering.
- Configurable parameters: `TopK` (default 20), `RrfK` (default 60), equal BM25/vector weights (1.0/1.0).

### Stage 3 — Security Filtering
- Server-side OData filter on `tenant_id` (always applied).
- Post-retrieval ACL enforcement: `Public`/`Internal` visibility passes; `Restricted` requires user membership in `allowed_groups` (case-insensitive).
- Defense-in-depth: second ACL check in `ChatOrchestrator.EnforceRestrictedContentExclusion` between retrieval and prompt assembly.

### Stage 4 — No-Evidence Detection
- `HasEvidence` flag: `false` when result count < `NoEvidenceMinResults` (default 3) or all scores below `NoEvidenceScoreThreshold` (default 0.3).
- No-evidence path triggers next-steps-only response with escalation signal.

### Evidence Index Schema
| Field | Type | Purpose |
|-------|------|---------|
| `chunk_id` | Key | `{evidenceId}_chunk_{index}` |
| `chunk_text` | Searchable | Primary content (EnMicrosoft analyzer) |
| `chunk_context` | Searchable | Surrounding context |
| `title` | Searchable | Source document title |
| `embedding_vector` | Vector | 1536-dim, HNSW cosine (M=4, efConstruction=400, efSearch=500) |
| `tenant_id` | Filterable | Tenant isolation |
| `evidence_id`, `source_system`, `source_type`, `status`, `updated_at`, `product_area`, `tags` | Filterable | Metadata filtering |
| `visibility`, `allowed_groups`, `access_label` | Filterable | ACL security trimming |
| `source_url` | Retrievable | Deep link to source |

### Retrieval Telemetry
- Logged per query: total raw results, ACL-filtered count, returned count, has-evidence flag, above-threshold count, duration, top-5 chunk IDs and scores, trace ID.

## Acceptance Criteria
- [x] Top-k includes relevant evidence for common support intents.
- [x] Unauthorized documents never appear in response payloads.
- [x] Retrieval response includes traceability metadata for audit.
- [x] No-evidence path is explicit and triggers next-step/escalation logic.
- [x] Integration tests validate ranking + ACL behavior.

## Non-Goals
- Custom search engine outside Azure AI Search in current scope.
- Global active-active retrieval infra in Phase 1.

## Phase Mapping
- Phase 1: hybrid evidence retrieval with security trimming.
- Phase 2: tuning profiles, synonyms, and semantic relevance optimization.
