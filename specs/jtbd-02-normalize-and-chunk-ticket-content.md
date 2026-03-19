# JTBD 02: Normalize, Chunk, and Enrich Support Content

## Job to Be Done
As a retrieval engineer, I need normalized and enriched content so retrieval returns high-signal evidence and reusable case knowledge.

## Scope
- Canonical normalization.
- Chunking and metadata enrichment.
- Case-card generation from solved tickets.
- Outputs for both Evidence Store and Case-Pattern Store pipelines.

## Requirements
- Normalize content into a canonical schema with tenant, source, ACL, and business metadata.
- Chunk documents by semantic boundaries and tickets by troubleshooting structure.
- Enrich with issue category, product/module, severity, environment, and key error tokens.
- Generate structured case cards with symptoms, root cause, resolution, verification, and escalation playbook hints.
- Version enrichment outputs to enable safe reprocessing:
  - Enrichment version is a monotonically increasing integer (`EnhancedEnrichmentService.CurrentEnrichmentVersion`, currently `2`).
  - Each `EvidenceChunkEntity` stores `EnrichmentVersion` (the version that produced it) and `ReprocessedAt` (timestamp of last reprocessing).
  - Chunk content hash includes the version: `SHA256({ChunkId}|{ChunkText}|{EnrichmentVersion})`. A version bump forces re-hash and re-indexing of all chunks on next sync.
  - Reprocessing is deterministic: same input + same version = same output hash.

## Acceptance Criteria
- [ ] Every chunk maps to parent source record and tenant.
- [ ] Metadata supports filterable retrieval and routing logic.
- [ ] Case cards are generated for solved-ticket candidates.
- [ ] Reprocessing with new enrichment version is deterministic.
- [ ] Evidence and pattern extraction pipelines use traceable lineage IDs.

## Non-Goals
- Full manual curation of every extracted field.
- OCR for all binary assets in Phase 1.

## Phase Mapping
- Phase 1: Canonical normalization + baseline chunking.
- Phase 2: richer enrichment + case-card quality hardening.
