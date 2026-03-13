# JTBD 04: Generate Grounded Answers, Next Steps, and Escalation Signals

## Job to Be Done
As a support user, I need responses that are grounded, actionable, and explicit about confidence so I can respond safely and escalate when needed.

## Scope
- OpenAI-backed orchestration for answer synthesis.
- Structured response contract.
- Next-step planning for low-confidence scenarios.
- Escalation recommendation signals.

## Required Response Contract
- answer_or_next_steps
- confidence score and rationale
- citations (source IDs + snippets)
- escalation recommendation (recommended flag, target team, reason)

## Requirements
- Use retrieval-grounded prompts and structured outputs.
- Require citations for factual/internal claims.
- If evidence is weak, output clarifying questions and diagnostic next steps.
- Produce escalation signal when thresholds/policies are met.
- Persist trace links between retrieved evidence and generated output.

## Acceptance Criteria
- [ ] Responses contain citations when factual claims are present.
- [ ] Low-confidence responses produce next-step guidance.
- [ ] Escalation recommendation includes reason and target team when triggered.
- [ ] Structured output is schema-validated and test-covered.
- [ ] Hallucination-prone paths degrade safely.

## Non-Goals
- Fully autonomous actions without human review.
- Fine-tuning as the primary reliability strategy in early phases.

## Phase Mapping
- Phase 1: grounded answers + next steps + escalation signaling.
- Phase 2: improved routing precision and handoff quality.
