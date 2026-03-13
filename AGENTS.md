# Project Operations Guide

## Product Goal
Build an intelligent support copilot with grounded answers, citations, escalation guidance, and continuous quality improvement.

## Core Architecture
- Evidence Store: Azure AI Search index for chunked source evidence.
- Case-Pattern Store: Azure AI Search index for distilled solved-case playbooks.
- Raw content store: Azure Blob/Files snapshots and extracted text.
- Operational store: Azure SQL for metadata, feedback, audit, connector config, and secret references.
- Secret store: Azure Key Vault for external connector credentials and webhook secrets.

## Identity and Access
- Authentication: Microsoft Entra ID (SSO).
- RBAC roles: `SupportAgent`, `SupportLead`, `Admin`, `EngineeringViewer`, `SecurityAuditor`.
- Enforce tenant isolation on every query, retrieval call, and admin action.
- Use security trimming (ACL filters) before model generation.

## Secret and Key Policy
- OpenAI API access uses a fixed server-side application setting.
- Store external-source secrets in Key Vault.
- Store only external secret references in Azure SQL when needed.
- Prefer Managed Identity for Azure service access.

## Infrastructure as Code Policy
- Every Azure resource must be represented in both Terraform and Azure ARM templates.
- Any Azure infrastructure change must update Terraform and ARM templates in the same PR/iteration.
- Keep environment-specific parameter files for dev/staging/prod current.
- Validate IaC changes before merge.

## Connector Freshness Model
- Azure DevOps: service hooks/webhooks + pull fallback.
- HubSpot: webhooks + ticket API sync fallback.
- ClickUp: webhooks with HMAC signature verification + pull fallback.
- SharePoint: Graph change notifications + delta sync + polling fallback.

## Build, Test, Run
```bash
dotnet restore
dotnet build
dotnet test
npm ci --prefix frontend
npm run lint --prefix frontend
npm run test --prefix frontend
npm run build --prefix frontend
dotnet run --project src/SmartKb.Api/SmartKb.Api.csproj
npm run dev --prefix frontend
```

## IaC Validation Commands
```bash
terraform fmt -recursive
terraform validate
az deployment group validate --resource-group <resource-group> --template-file infra/arm/main.json --parameters @infra/arm/parameters.dev.json
```

## Reliability and Quality Gates
- P95 answer-ready latency target: <= 8s for typical queries.
- Availability target: >= 99.5% for Phase 1.
- Keep ingestion idempotent and retry transient failures with backoff.
- Track nightly smoke eval and weekly gold-dataset eval regressions.
- Require correlation IDs and centralized logs/metrics/traces.

## MVP/Phase Map
- Phase 1 (MVP): chat + citations + triage + next steps + escalation suggestion + admin connectors + basic eval harness.
- Phase 2 (V1): pattern distillation/review + advanced filters + stronger observability.
- Phase 3 (V2+): policy automation and deeper routing optimization.

## Common Debug Commands
```bash
git status
git log --oneline -n 20
rg -n "TODO|FIXME|HACK|NotImplemented" .
rg -n "citation|acl|rbac|tenant|escalation|handoff|pattern|key vault|managed identity|correlation|terraform|arm" src backend frontend infra
```

