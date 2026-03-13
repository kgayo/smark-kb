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
- Enable lead/admin approval to promote patterns to approved/high-trust state.
- Support version history and deprecation/replacement links.
- Retrieve approved patterns alongside evidence during answer generation.

## Acceptance Criteria
- [ ] Solved-ticket pipeline creates draft patterns with traceability.
- [ ] Approval workflow changes trust level and audit records.
- [ ] Deprecated patterns are excluded or clearly marked during retrieval.
- [ ] Pattern usage and reuse metrics are available.
- [ ] Pattern retrieval improves response consistency for repeat issues.

## Non-Goals
- Pattern publication without human governance.
- One-time manual pattern creation as sole mechanism.

## Phase Mapping
- Phase 2: pattern distillation + approval workflow.
- Phase 3: advanced lifecycle automation.
