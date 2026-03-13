# JTBD 07: Administer External Source Connections Securely

## Job to Be Done
As an admin, I need secure self-serve connector operations so source onboarding and maintenance do not require engineering intervention.

## Scope
- Connector lifecycle management.
- Auth setup and credential rotation.
- Field mapping and transform rules.
- Sync controls, preview/validation, and diagnostics.

## Requirements
- Support connector add/edit/delete/enable/disable per tenant.
- Support OAuth, token/PAT, and private key/service account auth as applicable.
- Provide scope selection per source (projects/sites/workspaces/pipelines).
- Provide field mapping from source schema to canonical schema with transform rules.
- Provide controls for full backfill, incremental sync now, schedule, pause/resume.
- Provide preview of normalized records and required-field validation.
- Provide health diagnostics: run status, webhook status, rate-limit alerts, dead-letter visibility.
- Store secrets in Key Vault and metadata/refs in SQL only.

## Acceptance Criteria
- [ ] Admin can complete connector setup and test connection without engineer help.
- [ ] Field mapping errors are surfaced before sync activation.
- [ ] Sync run status and failures are visible with actionable diagnostics.
- [ ] Credential rotation can occur without redeploy.
- [ ] Unauthorized users cannot access admin connector actions.

## Non-Goals
- Custom connector SDK for arbitrary systems in Phase 1.
- Exposing raw secrets in UI/log/export paths.

## Phase Mapping
- Phase 1: connector setup, sync controls, and baseline diagnostics.
- Phase 2: deeper governance and mapping automation.
