0a. Study @IMPLEMENTATION_PLAN.md. Only implement items marked `- [ ]` (uncompleted). Do NOT create new TECH-*, BUG-*, or backlog items.
0b. Pick the highest-priority incomplete item and verify current behavior in code before editing.
0c. Keep implementation aligned to two-store architecture (Evidence + Case-Pattern) and phase targets.
0d. Once all `- [ ]` items are marked `- [x]`, update Status to PROJECT COMPLETE and STOP. Do not scan for new work, do not invent refactoring or cleanup tasks.

1. Implement the selected requirement completely.
2. Add or update unit tests and integration tests for changed behavior.
3. Run focused end-to-end checks for user-critical journeys (chat, escalation, connector admin) when touched.
4. Commit with a clear message tied to the plan item.

999. Every factual/internal answer must include citations.
9999. Enforce ACL/security trimming before generation; never send restricted content to the model.
99999. If evidence is insufficient, return next-step guidance or escalation path, not fabricated answers.
999999. Support escalation recommendation with structured handoff fields when criteria are met.
9999999. Support admin connector auth via OAuth/PAT/private key where applicable.
99999999. Store external connector secrets in Azure Key Vault; keep the fixed OpenAI key server-side in application settings; Azure SQL stores secret references and metadata only.
999999999. Use Entra ID auth + RBAC + tenant isolation for all protected endpoints.
9999999999. Propagate correlation IDs and emit logs/metrics/traces for each request path.
99999999999. Keep @IMPLEMENTATION_PLAN.md current at end of iteration.
999999999999. For any Azure resource change, update and validate both Terraform and ARM templates in the same change set.
9999999999999. Keep @AGENTS.md operational only.
99999999999999. No placeholders for required behavior.

