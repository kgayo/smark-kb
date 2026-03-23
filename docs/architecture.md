# Architecture

Smart KB uses a two-store RAG (Retrieval-Augmented Generation) architecture with an Azure backend.

## System Overview

```
                    ┌──────────────────────┐
                    │   React Frontend     │
                    │   (Azure Static      │
                    │    Web App)           │
                    └─────────┬────────────┘
                              │ MSAL auth + REST
                              ▼
                    ┌──────────────────────┐
                    │   ASP.NET Core API   │
                    │   (Azure App Service)│
                    └──┬────┬────┬────┬────┘
                       │    │    │    │
              ┌────────┘    │    │    └────────┐
              ▼             ▼    ▼             ▼
        ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐
        │ Azure AI │ │ Azure SQL│ │  OpenAI  │ │ Key Vault│
        │ Search   │ │ Database │ │  GPT-4o  │ │          │
        └──────────┘ └──────────┘ └──────────┘ └──────────┘
              ▲
              │ index
        ┌──────────────────────┐     ┌──────────────┐
        │  Ingestion Worker    │◄────│ Azure Service │
        │  (Azure App Service) │     │ Bus Queue     │
        └──────────┬───────────┘     └──────────────┘
                   │ fetch
        ┌──────────┴───────────┐
        │  External Sources    │
        │  (ADO, SharePoint,   │
        │   HubSpot, ClickUp)  │
        └──────────────────────┘
```

## Two-Store Model

### Evidence Store
- Source: ingested tickets, articles, documents from external connectors
- Storage: Azure AI Search index with hybrid retrieval (BM25 + vector + semantic reranking)
- Embeddings: `text-embedding-3-large` at 1536 dimensions
- Chunking: 512 tokens per chunk, 64 token overlap, structural boundary awareness
- ACL enforcement: security trimming applied before retrieval results reach the LLM

### Case-Pattern Store
- Source: auto-distilled patterns from resolved cases
- Storage: separate Azure AI Search index + SQL metadata
- Trust model: Draft → Reviewed → Approved → Deprecated
- Governance: human-in-the-loop approval before patterns influence answers
- Maintenance: contradiction detection, staleness checks, quality scoring

## Request Flow (Chat Query)

1. User sends query via React frontend (authenticated with MSAL / Entra ID)
2. API extracts tenant context, user ACL groups, and correlation ID
3. Pre-retrieval query classification (gpt-4o-mini) biases search filters
4. Hybrid retrieval runs against both Evidence and Pattern indexes with OData filters
5. ACL enforcement removes results the user lacks access to
6. PII redaction scans remaining content per tenant policy
7. Session summarization compresses long conversation history
8. GPT-4o generates grounded answer with citations
9. Response includes confidence score, next steps, and escalation recommendation if needed

## Security Model

- **Authentication**: Microsoft Entra ID (Azure AD) with JWT bearer tokens
- **Authorization**: 5-role RBAC model (SupportAgent, SupportLead, Admin, EngineeringViewer, SecurityAuditor) with 14 permission strings
- **Tenant isolation**: enforced at middleware level via `tid` claim — all queries, admin actions, and telemetry are tenant-scoped
- **ACL trimming**: user's security group memberships filter search results before prompt assembly
- **PII redaction**: per-tenant policy (redact/detect/disabled) with custom regex patterns
- **Secrets**: external connector credentials stored in Azure Key Vault; OpenAI key is a fixed server-side application setting
- **Audit**: all mutations emit audit events with actor, tenant, correlation ID, and timestamp

## Ingestion Pipeline

1. **Trigger**: webhook from source system, scheduled cron sync, or manual sync-now
2. **Queue**: `SyncJobMessage` published to Azure Service Bus
3. **Process**: `SyncJobProcessor` runs with checkpoint tracking, idempotency, error capping
4. **Normalize**: raw records converted to `CanonicalRecord` schema
5. **Extract**: binary documents (PDF, DOCX, PPTX, XLSX) have text extracted inline
6. **Chunk**: content split into `EvidenceChunk` records with structural boundary detection
7. **Embed**: chunks embedded via `text-embedding-3-large`
8. **Index**: chunks indexed into Azure AI Search
9. **Dead-letter**: failed messages routed to DLQ for admin inspection

## Infrastructure

All Azure resources are defined in both Terraform and ARM templates (kept in parity):

| Resource | Naming | Purpose |
|----------|--------|---------|
| Resource Group | `rg-smartkb-{env}` | Container for all resources |
| App Service (API) | `app-smartkb-api-{env}` | .NET API hosting |
| App Service (Ingestion) | `app-smartkb-ingestion-{env}` | Background worker hosting |
| Static Web App | `stapp-smartkb-{env}` | React frontend hosting |
| Azure SQL | `sql-smartkb-{env}` / `sqldb-smartkb-{env}` | Relational data |
| Azure AI Search | `srch-smartkb-{env}` | Hybrid search indexes |
| Service Bus | `sb-smartkb-{env}` | Ingestion job queue |
| Key Vault | `kv-smartkb-{env}` | Connector secrets |
| Storage Account | `stsmartkb{env}` | Blob storage for raw content |
| Application Insights | `appi-smartkb-{env}` | Telemetry and monitoring |

All App Service instances use SystemAssigned managed identity with RBAC role assignments (no connection strings with embedded keys).
