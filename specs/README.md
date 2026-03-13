# Specs Index

This file is the index and release map for the specs directory.
The detailed requirements live in the numbered jtbd-XX-...md files.
Update this file when the overall spec inventory or phase mapping changes.

The specs are now aligned to the PRD bundle and cover:
- multi-source ingestion with resilient freshness (webhooks, delta, fallback polling)
- normalization/chunking/enrichment and case-card extraction
- hybrid retrieval with security trimming and telemetry
- grounded generation with next steps and escalation signals
- agent UX + admin UX flows
- feedback, outcomes, and evaluation harness
- secure connector administration and secret management
- escalation routing and structured handoff
- case-pattern governance lifecycle
- security, privacy, RBAC, and tenant isolation
- infrastructure-as-code governance with Terraform and ARM templates

## Release Mapping
- Phase 1 (MVP): core chat/citations, triage, next steps, escalation suggestion, admin connectors, baseline eval/security, baseline IaC.
- Phase 2 (V1): pattern lifecycle workflows, advanced filters/diagnostics, stronger observability/governance, IaC hardening.
- Phase 3 (V2+): policy automation and optimization at scale.

