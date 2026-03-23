# JTBD 09: Govern Case-Pattern Lifecycle

## Job to Be Done
As a support lead, I need solved cases distilled into governed patterns so repeat issues are resolved faster and consistently.

## Scope
- Pattern extraction from solved tickets.
- Pattern review and approval workflow.
- Pattern versioning, deprecation, and supersession.
- Pattern retrieval integration with chat orchestration.

## Requirements
- Extract pattern fields: canonical problem, root cause, resolution, workaround, verification, escalation playbook, applicability constraints.
- Create draft patterns automatically with low-trust status.
- Pattern trust lifecycle uses a 4-state model (`TrustLevel` enum):
  1. **Draft** — auto-created by distillation; not surfaced in retrieval.
  2. **Reviewed** — lead has reviewed content; surfaced with lower weight.
  3. **Approved** — lead/admin has approved; full retrieval weight with trust boost.
  4. **Deprecated** — superseded or invalid; excluded from retrieval and deleted from search index.
- Transitions: Draft → Reviewed → Approved (forward); any state → Deprecated (terminal). Re-indexing occurs on all trust transitions except deprecation (which deletes from index).
- Support version history and deprecation/replacement links.
- Retrieve approved patterns alongside evidence during answer generation.

## Acceptance Criteria
- [x] Solved-ticket pipeline creates draft patterns with traceability.
- [x] Approval workflow changes trust level and audit records.
- [x] Deprecated patterns are excluded or clearly marked during retrieval.
- [x] Pattern usage and reuse metrics are available:
  - Usage detection: `PatternMaintenanceService` queries `AnswerTraceEntity` records for pattern citations within a configurable `UnusedDaysThreshold` window.
  - Patterns not cited in any answer trace within the threshold are flagged as "Unused" maintenance tasks.
  - Maintenance tasks stored in `PatternMaintenanceTaskEntity` with `MetricsJson` containing citation counts and age metrics.
  - Note: A formal per-pattern usage analytics API (citation counts by time window) is tracked as P3-012.
- [x] Pattern retrieval improves response consistency for repeat issues.

## Non-Goals
- Pattern publication without human governance.
- One-time manual pattern creation as sole mechanism.

## Phase Mapping
- Phase 2: pattern distillation + approval workflow.
- Phase 3: advanced lifecycle automation.
