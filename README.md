# Smart KB

Intelligent support copilot that provides grounded answers with citations, proposes next steps, and routes escalations — powered by a two-store RAG architecture (Evidence Store + Case-Pattern Store) on Azure.

## What It Does

Smart KB ingests support knowledge from multiple sources (Azure DevOps, SharePoint, HubSpot, ClickUp), normalizes and indexes it, then helps support agents find answers, follow resolution patterns, and escalate when needed. Every answer includes citations back to source evidence, and a feedback loop continuously improves pattern quality.

## Architecture

| Layer | Technology | Purpose |
|-------|-----------|---------|
| **Frontend** | React 18 + TypeScript + Vite | Chat UI, admin pages, pattern governance |
| **API** | ASP.NET Core (.NET 10) Minimal API | Orchestration, retrieval, admin endpoints |
| **Ingestion** | Background Service + Azure Service Bus | Connector sync, normalization, indexing |
| **Search** | Azure AI Search (BM25 + vector + semantic reranking) | Hybrid retrieval across both stores |
| **Generation** | OpenAI GPT-4o | Grounded answer generation with citations |
| **Database** | Azure SQL + EF Core | Metadata, sessions, feedback, audit, governance |
| **Auth** | Microsoft Entra ID (MSAL) | SSO, RBAC, tenant isolation |
| **Secrets** | Azure Key Vault | Connector credentials, API keys |
| **IaC** | Terraform + ARM templates | Full Azure provisioning (kept in parity) |

## Quick Start

### Prerequisites

- .NET 10 SDK
- Node.js 20+
- Azure CLI (for Key Vault/managed identity access)
- Git

### Backend

```bash
dotnet restore
dotnet build
dotnet test                    # ~1800 backend tests
dotnet run --project src/SmartKb.Api/SmartKb.Api.csproj
```

### Frontend

```bash
cd frontend
npm ci
npm run test                   # ~258 tests
npm run dev                    # localhost:3000, proxies /api to localhost:5000
```

### Full Build (CI-equivalent)

```bash
dotnet restore && dotnet build && dotnet test
npm ci --prefix frontend && npm run build --prefix frontend
```

## Pages

The frontend has multiple admin and user-facing pages. See [docs/pages/](docs/pages/) for detailed per-page documentation.

| Page | Path | Role | Description |
|------|------|------|-------------|
| [Chat](docs/pages/chat.md) | `/` | All users | Main support assistant — ask questions, get cited answers, escalate |
| [Admin (Connectors)](docs/pages/admin-connectors.md) | `/admin` | Admin | Manage data source connectors, field mappings, sync schedules |
| [Pattern Governance](docs/pages/pattern-governance.md) | `/patterns` | Admin, SupportLead | Review and govern case patterns through 4-state trust lifecycle |
| [Diagnostics](docs/pages/diagnostics.md) | `/diagnostics` | Admin | Monitor webhooks, dead-letter queue, SLO targets, service health |
| [Synonym Management](docs/pages/synonym-management.md) | `/synonyms` | Admin | Manage domain vocabulary synonyms for improved search recall |
| [Audit & Compliance](docs/pages/audit-compliance.md) | `/audit` | Admin | Browse audit events, filter, and export NDJSON for compliance |
| [Gold Dataset](docs/pages/gold-dataset.md) | `/gold-cases` | Admin | Manage eval gold cases — create, edit, export JSONL for eval CLI |

## Deployment

See [docs/deployment.md](docs/deployment.md) for full deployment instructions covering:

- Infrastructure provisioning (Terraform + ARM)
- CI/CD pipelines (GitHub Actions)
- Environment configuration (dev / staging / prod)
- Manual and automated deployment workflows

## API Reference

See [docs/api-reference.md](docs/api-reference.md) for the complete list of 123 endpoints across 21 functional groups.

## Project Structure

```
smart-kb/
├── src/
│   ├── SmartKb.Api/              # ASP.NET Core minimal API
│   ├── SmartKb.Contracts/        # Shared DTOs, enums, interfaces, config
│   ├── SmartKb.Data/             # EF Core, SQL entities, migrations
│   ├── SmartKb.Ingestion/        # BackgroundService + Service Bus processor
│   └── SmartKb.Eval.Cli/         # Evaluation CLI for CI gold-dataset runs
├── frontend/                     # React + Vite + TypeScript
├── tests/                        # Unit + integration tests
├── infra/
│   ├── terraform/                # Terraform modules + env-specific tfvars
│   ├── arm/                      # ARM templates (parity with Terraform)
│   └── scripts/                  # Bootstrap and validation scripts
├── specs/                        # JTBD requirement specs (11 files)
├── docs/                         # Documentation (this README + per-page guides)
│   └── pages/                    # Per-page UI documentation
├── .github/workflows/            # CI, infra validation, deploy, eval
├── IMPLEMENTATION_PLAN.md        # Prioritized backlog (Ralph Loop state)
├── AGENTS.md                     # Operational guide for coding agents
├── loop.sh                       # Ralph Loop script (Claude Code)
└── loop-codex.sh                 # Ralph Loop script (Codex)
```

## Development Methodology

This project uses the [Ralph Loop](https://ghuntley.com/ralph/) for AI-assisted development. Run `./loop.sh` to start an autonomous plan-then-build loop. See [ralph-setup-guide.md](ralph-setup-guide.md) for details.

## Documentation

| Document | Purpose |
|----------|---------|
| [docs/getting-started.md](docs/getting-started.md) | Local development setup |
| [docs/deployment.md](docs/deployment.md) | Deployment and infrastructure |
| [docs/architecture.md](docs/architecture.md) | System architecture overview |
| [docs/api-reference.md](docs/api-reference.md) | API endpoint reference |
| [docs/pages/](docs/pages/) | Per-page UI documentation |
| [AGENTS.md](AGENTS.md) | Coding agent operational guide |
| [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md) | Backlog and status |
| [specs/](specs/) | Requirement specifications |
