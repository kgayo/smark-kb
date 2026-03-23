# JTBD 01: Unify Multi-Source Knowledge into a Canonical Corpus

## Job to Be Done
As a support operations lead, I need content from multiple systems unified into one canonical corpus so support can search one trusted source of truth.

## Scope
- Connectors: Azure DevOps wiki/work items, HubSpot tickets, SharePoint docs, ClickUp docs/tasks.
- Historical backfill + incremental sync.
- Event-driven freshness plus fallback polling.
- Admin configuration without redeploying services.

## Requirements
- Ingest into canonical records with source IDs, deep links, timestamps, ACL metadata, and tenant IDs.
- Use source-native freshness where available:
  - ADO service hooks/webhooks
  - HubSpot webhooks
  - ClickUp webhooks with signature verification
  - SharePoint Graph delta + change notifications
- Support polling fallback when webhooks fail or are unavailable.
- Ensure idempotent processing and replay safety.
- Deduplicate content at ingestion time using SHA-256 content hashes:
  - Raw content: `RawContentSnapshotEntity.ContentHash` compared before blob upload; unchanged content skips re-upload.
  - Chunks: `EvidenceChunkEntity` hash computed as `SHA256({ChunkId}|{ChunkText}|{EnrichmentVersion})`; upsert skips re-indexing when hash matches.
  - Sync runs: minute-granularity idempotency keys (`scheduled-{connectorId}-{yyyyMMddHHmm}`) prevent duplicate scheduled triggers.
- Store connector secrets in Azure Key Vault; store only secret references/metadata in Azure SQL.

## Acceptance Criteria
- [x] Backfill handles 3k+ artifacts with no data loss.
- [x] Incremental sync captures creates/updates reliably.
- [x] Webhook signatures are validated for providers that support them.
- [x] Delta sync/checkpointing works for SharePoint Graph.
- [x] Replay of events does not create duplicates.
- [x] Admin can create/edit/test/enable/disable connectors without deployment.

## Non-Goals
- Bi-directional writes to all source systems.
- Guaranteed real-time (<1 minute) sync for every connector.

## Phase Mapping
- Phase 1: ADO + SharePoint initial ingestion.
- Phase 2: HubSpot + ClickUp connectors and hardening.
