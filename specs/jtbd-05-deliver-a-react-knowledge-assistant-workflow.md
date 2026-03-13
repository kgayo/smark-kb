# JTBD 05: Deliver Agent Chat UX and Admin Workflow UX

## Job to Be Done
As a support team member, I need a chat interface with evidence and escalation controls, while admins need self-serve connector operations.

## Scope
- Agent chat UI with evidence drawer and follow-up handling.
- Escalation actions and draft handoff review UI.
- Feedback controls and outcome capture.
- Admin connector management screens.

## Requirements
- Agent view must show answer, confidence, citations, and evidence drawer.
- Support follow-up questions with session context.
- Provide escalation CTA and handoff-note preview workflow.
- Capture feedback: thumbs up/down, reason codes, optional correction text, and outcome events.
- Admin view must support connector setup, scope selection, mapping, sync controls, preview/validation, and diagnostics.

## Acceptance Criteria
- [ ] Agent can complete answer-with-citations workflow end-to-end.
- [ ] Evidence drawer exposes snippet + source location + timestamp + access label.
- [ ] Escalation flow supports review before draft creation.
- [ ] Feedback and outcome events persist with trace IDs.
- [ ] Admin can configure connectors and run sync/validation from dashboard.

## Non-Goals
- Full omnichannel support in early phases.
- Rich theming/custom branding in MVP.

## Phase Mapping
- Phase 1: core agent UX + admin connector baseline.
- Phase 2: expanded admin diagnostics and workflow polish.
