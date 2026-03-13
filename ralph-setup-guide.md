# Ralph Loop Setup Guide

## What Ralph Is

This repo uses a Ralph-style development loop: a small shell loop repeatedly feeds Claude Code a prompt file, and the durable state lives on disk in:

- `specs/`
- `IMPLEMENTATION_PLAN.md`
- `AGENTS.md`
- the source tree under `src/`

Each iteration gets a fresh context window, so the loop works only if the important state is written back to those files.

## Core Ralph Principles

These are the Ralph principles this repo follows, based on Geoff Huntley's `ralph` and `loop` posts:

- Keep the main loop simple. One prompt, one iteration, one highest-priority task.
- Keep the main context small. Put durable knowledge on disk instead of relying on chat memory.
- One loop should tackle one clear piece of work, not an entire roadmap.
- Use subagents or parallel discovery to offload broad reads and searches.
- Rely on backpressure. Tests, lint, typechecks, builds, and validations must reject bad work.
- Treat the plan as disposable. If `IMPLEMENTATION_PLAN.md` drifts, regenerate it from the specs and code.
- Keep improving the prompts and operational guide when the loop fails in repeatable ways.

## Repo Layout

```text
smart-kb/
|-- loop.sh
|-- PROMPT_plan.md
|-- PROMPT_build.md
|-- AGENTS.md
|-- IMPLEMENTATION_PLAN.md
|-- specs/
|   |-- README.md
|   |-- jtbd-01-unify-support-knowledge-ingestion.md
|   |-- jtbd-02-normalize-and-chunk-ticket-content.md
|   |-- jtbd-03-retrieve-relevant-evidence-with-azure-ai-search.md
|   |-- jtbd-04-generate-grounded-answers-with-openai-api.md
|   |-- jtbd-05-deliver-a-react-knowledge-assistant-workflow.md
|   |-- jtbd-06-close-the-feedback-loop-through-mvp4.md
|   |-- jtbd-07-administer-external-source-connections-securely.md
|   |-- jtbd-08-route-escalations-with-structured-handoff.md
|   |-- jtbd-09-govern-case-pattern-lifecycle.md
|   |-- jtbd-10-enforce-security-privacy-and-tenant-isolation.md
|   `-- jtbd-11-provision-and-maintain-azure-infrastructure-as-code.md
`-- src/
```

If the codebase grows, keep application source under `src/`. Do not document `src/lib/*` as a special requirement unless that directory actually becomes a project convention.

## How `specs/` Works In This Repo

This repo does not use placeholder specs like `feature-a.md` or `feature-b.md`.

Use the current structure instead:

- `specs/README.md` is the index and release map.
- Each `specs/jtbd-XX-...md` file is one requirement slice with:
  - Job to Be Done
  - Scope
  - Requirements
  - Acceptance Criteria
  - Non-Goals
  - Phase Mapping

### What To Edit

- Change the relevant `jtbd-XX` file when the requirement itself changes.
- Change `specs/README.md` when the release mapping or overall spec inventory changes.
- Do not create a new spec file unless the work is a genuinely separate requirement slice.

### How To Read `specs/README.md`

`specs/README.md` is not a duplicate spec. It is the summary page that tells Ralph and humans:

- what the full spec set covers
- how the JTBD specs map to Phase 1, Phase 2, and Phase 3+

If you only read `specs/README.md`, you get the map. If you need the actual requirement details, read the relevant `jtbd-XX` file.

## Prompt Roles In This Repo

### `PROMPT_plan.md`

Planning mode should:

- read `specs/*`
- read `IMPLEMENTATION_PLAN.md`
- inspect the current implementation under `src/*`
- compare code against the specs
- update `IMPLEMENTATION_PLAN.md`
- avoid code changes

### `PROMPT_build.md`

Build mode should:

- read `specs/*` and `IMPLEMENTATION_PLAN.md`
- pick the highest-priority incomplete item
- verify current behavior before editing
- implement the requirement completely
- run focused tests for the changed behavior
- keep `IMPLEMENTATION_PLAN.md` current

### `AGENTS.md`

`AGENTS.md` is operational only. Keep it brief and stable. It should contain:

- architecture guardrails
- build, test, and run commands
- IaC validation commands
- high-value debugging commands

Do not use `AGENTS.md` for status notes or backlog items.

## Repo-Specific Guardrails

Ralph in this repo should preserve these rules:

- Security trimming happens before generation.
- Tenant isolation is mandatory across retrieval, admin actions, and telemetry.
- External connector secrets belong in Azure Key Vault.
- The fixed OpenAI API key stays server-side in application settings or equivalent server configuration.
- Azure resource changes must update both Terraform and ARM artifacts.
- `IMPLEMENTATION_PLAN.md` is the prioritized backlog, not a diary.

## Running The Loop

### Planning Mode

```bash
./loop.sh plan 3
```

Use planning mode when:

- the specs changed
- the implementation plan drifted
- you need a fresh gap analysis

### Build Mode

```bash
./loop.sh build 10
```

You can also run:

```bash
./loop.sh 10
./loop.sh
```

Use build mode when the plan is good enough and Ralph should start implementing the highest-priority item.

## Recommended Workflow

1. Refine the relevant `specs/jtbd-XX-...md` files.
2. Update `specs/README.md` if the phase mapping changed.
3. Run `./loop.sh plan 3`.
4. Review `IMPLEMENTATION_PLAN.md`.
5. Run `./loop.sh build 10`.
6. Tighten prompts or `AGENTS.md` only when you observe repeated failure patterns.

## Why This Structure Works

This layout matches the main Ralph idea: keep the loop simple and keep the durable context on disk.

- Specs define what should exist.
- The implementation plan defines what is next.
- AGENTS defines how to operate safely.
- The source tree under `src/` shows what already exists.

That keeps each iteration focused instead of forcing the model to reconstruct the whole project from scratch.

## References

- [Geoff Huntley - Ralph](https://ghuntley.com/ralph/)
- [Geoff Huntley - Everything is a Ralph Loop](https://ghuntley.com/loop/)
- [How to Ralph Wiggum](https://github.com/ghuntley/how-to-ralph-wiggum)
