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

## Acceptance Criteria
- [ ] Top-k includes relevant evidence for common support intents.
- [ ] Unauthorized documents never appear in response payloads.
- [ ] Retrieval response includes traceability metadata for audit.
- [ ] No-evidence path is explicit and triggers next-step/escalation logic.
- [ ] Integration tests validate ranking + ACL behavior.

## Non-Goals
- Custom search engine outside Azure AI Search in current scope.
- Global active-active retrieval infra in Phase 1.

## Phase Mapping
- Phase 1: hybrid evidence retrieval with security trimming.
- Phase 2: tuning profiles, synonyms, and semantic relevance optimization.
