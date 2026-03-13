# JTBD 08: Route Escalations with Structured Handoff

## Job to Be Done
As a support agent, I need escalation recommendations and high-quality handoff drafts so downstream teams can act quickly.

## Scope
- Escalation trigger logic.
- Target-team recommendation.
- Structured handoff note generation.
- Draft ticket/task creation workflows.

## Requirements
- Determine escalation need using confidence, policy, and issue characteristics.
- Recommend target team with rationale.
- Generate handoff draft including summary, repro steps, logs/IDs requested, suspected component, severity, and evidence links.
- Support draft creation flow for Azure DevOps or ClickUp with human review before submission.
- Track escalation outcomes (accepted/rerouted/resolved) for routing improvement.

## Acceptance Criteria
- [ ] Escalation suggestion includes team + reason.
- [ ] Handoff draft contains required structured fields.
- [ ] Agent can review/edit before creating external draft.
- [ ] Outcome telemetry is captured and linked to source session.
- [ ] Routing quality metrics are measurable over time.

## Non-Goals
- Fully autonomous escalation without agent confirmation.
- Auto-closing escalations based only on model output.

## Phase Mapping
- Phase 1: escalation suggestion + draft handoff.
- Phase 2: routing optimization from outcome data.
