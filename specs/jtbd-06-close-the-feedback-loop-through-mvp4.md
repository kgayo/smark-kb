# JTBD 06: Close the Loop with Feedback, Evaluation, and Outcome Analytics

## Job to Be Done
As a support lead, I need measurable feedback and evaluation loops so answer quality and routing improve over time.

## Scope
- In-app feedback capture.
- Outcome tracking (resolved/escalated/accepted/rerouted).
- Gold dataset evaluation harness.
- Regression reporting and release gating.

## Requirements
- Log feedback: thumbs, reason codes, optional free text, and corrected answer proposals.
- Log outcomes: resolved_without_escalation, escalated, target team, acceptance, time-to-assign, time-to-resolve.
- Maintain a gold dataset for retrieval/generation/routing evaluations.
- Run nightly smoke eval and weekly full eval with trend reporting.
- Block release or flag regressions when quality thresholds degrade.
- Default quality thresholds (configurable via `EvalSettings`):
  - Groundedness: >= 0.80 (80%)
  - Citation coverage: >= 0.70 (70%)
  - Routing accuracy: >= 0.60 (60%)
  - Maximum no-evidence rate: <= 0.25 (25%)
- Violations trigger GitHub Actions `::error` annotations; regressions trigger `::warning` or `::error` based on delta magnitude.

## Acceptance Criteria
- [ ] Feedback and outcome events are queryable per tenant.
- [ ] Weekly quality report includes groundedness (>= 0.80), citation coverage (>= 0.70), routing accuracy (>= 0.60), and no-evidence rate (<= 0.25).
- [ ] Evaluation runs compare against last known good baseline.
- [ ] Improvement actions are traceable to measured deltas.
- [ ] Regression alerts are visible to support lead/engineering.

## Non-Goals
- Unsupervised self-modifying model behavior.
- Evaluation without human review checkpoints.

## Phase Mapping
- Phase 1: baseline feedback + basic eval harness.
- Phase 2+: richer evaluation and governance automation.
