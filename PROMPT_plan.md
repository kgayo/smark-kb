0a. Study `specs/*` with up to 300 parallel Sonnet subagents to learn the product requirements.
0b. Study @IMPLEMENTATION_PLAN.md (if present) to understand prior decisions and what remains.
0c. Study source code in `src/*` and map implementation coverage to specs.
0d. Treat this as a two-store RAG system: Evidence Store + Case-Pattern Store.

1. Compare code vs specs and update @IMPLEMENTATION_PLAN.md as a prioritized backlog.
2. Use Sonnet subagents for broad discovery and Opus for final synthesis/prioritization.
3. Plan in release slices:
   - Phase 1 (MVP): chat + citations, triage, next steps, escalation suggestion, admin connectors, basic eval harness
   - Phase 2 (V1): case-pattern distillation + approval workflow + advanced filters + stronger observability
   - Phase 3 (V2+): deeper automation and policy-driven routing improvements
4. Enforce architecture constraints:
   - OpenAI API for orchestration/generation using a fixed server-side application key
   - Azure AI Search for hybrid retrieval (BM25 + vector + semantic reranking)
   - Azure SQL for metadata, feedback, audit, and secret references (no raw secrets)
   - Azure Key Vault for external connector secrets
   - Microsoft Entra ID for SSO and RBAC
   - .NET backend + React frontend
   - Infrastructure as Code must be maintained in both Terraform and Azure ARM templates for all Azure resources

IMPORTANT:
- Plan only. Do NOT implement code.
- Validate existence with code search before marking gaps.
- Specs are source of truth; patch specs first if missing/ambiguous.
- Include security trimming, tenant isolation, and audit trails as first-class items.
- Include ingestion robustness items: webhook validation, delta sync, polling fallback, idempotency.
- Include evaluation gates: gold dataset runs, regression checks, and latency/reliability SLO tracking.
- Include IaC governance: every Azure resource change must update Terraform and ARM templates in the same iteration.

ULTIMATE GOAL:
Deliver an intelligent support assistant that provides grounded answers with citations, proposes next steps and escalation routing when needed, stays fresh via resilient ingestion, and improves safely through feedback + evaluation.


