# Smart KB Architecture

## 1. High-Level System Architecture

```mermaid
graph TB
    subgraph Users["Users"]
        SA[Support Agent]
        SL[Support Lead]
        ADM[Admin]
    end

    subgraph Frontend["Frontend (React SPA)"]
        CHAT[Chat Page]
        ADMIN_UI[Admin Dashboard]
        GOV[Pattern Governance]
        DIAG[Diagnostics]
        AUDIT_UI[Audit & Compliance]
        COST[Cost Controls]
        PRIV[Privacy Admin]
    end

    subgraph Auth["Authentication"]
        ENTRA[Microsoft Entra ID]
        MSAL[MSAL Browser SDK]
    end

    subgraph Backend["Backend (.NET 10)"]
        API[SmartKb.Api<br/>ASP.NET Core Minimal API]
        ING[SmartKb.Ingestion<br/>Background Worker]
    end

    subgraph Azure["Azure Services"]
        SQL[(Azure SQL Database)]
        SEARCH[Azure AI Search]
        SB[Service Bus Queue]
        KV[Azure Key Vault]
        BLOB[Azure Blob Storage]
        AI[Application Insights]
    end

    subgraph External["External Systems"]
        OPENAI[OpenAI API<br/>GPT-4o + text-embedding-3-large]
        ADO_EXT[Azure DevOps]
        SP_EXT[SharePoint]
        HB_EXT[HubSpot]
        CU_EXT[ClickUp]
    end

    SA & SL & ADM --> MSAL
    MSAL --> ENTRA
    ENTRA -->|JWT Token| Frontend
    Frontend -->|REST API + Bearer Token| API

    API --> SQL
    API --> SEARCH
    API --> SB
    API --> KV
    API --> BLOB
    API --> OPENAI
    API --> AI

    SB -->|ingestion-jobs queue| ING
    ING --> SQL
    ING --> SEARCH
    ING --> KV
    ING --> BLOB
    ING --> AI

    ING -->|Fetch Data| ADO_EXT & SP_EXT & HB_EXT & CU_EXT
    ADO_EXT & SP_EXT & HB_EXT & CU_EXT -->|Webhooks| API
```

---

## 2. Azure Infrastructure

```mermaid
graph TB
    subgraph RG["Resource Group: rg-smartkb-{env}"]
        subgraph Compute["Compute"]
            PLAN[App Service Plan<br/>plan-smartkb-{env}<br/>B1/P1v3]
            API_APP[Web App: API<br/>app-smartkb-api-{env}<br/>.NET 10]
            ING_APP[Web App: Ingestion<br/>app-smartkb-ingestion-{env}<br/>.NET 10]
            SWA[Static Web App<br/>stapp-smartkb-{env}<br/>React SPA]
        end

        subgraph Data["Data"]
            SQL_SVR[SQL Server<br/>sql-smartkb-{env}]
            SQL_DB[(SQL Database<br/>sqldb-smartkb-{env}<br/>Basic/S1)]
            STORAGE[Storage Account<br/>stsmartkb{env}]
            BLOB_CTR[Blob Container<br/>raw-content]
        end

        subgraph Search["Search & Messaging"]
            SRCH[Azure AI Search<br/>srch-smartkb-{env}<br/>basic/standard]
            SB_NS[Service Bus Namespace<br/>sb-smartkb-{env}]
            SB_Q[Queue: ingestion-jobs<br/>Max Delivery: 10<br/>Dead-letter enabled]
        end

        subgraph Security["Security"]
            KV[Key Vault<br/>kv-smartkb-{env}<br/>RBAC-enabled]
            CMK_ID[CMK Identity<br/>id-smartkb-cmk-{env}<br/>conditional]
        end

        subgraph Monitoring["Monitoring & Alerting"]
            LOG[Log Analytics<br/>log-smartkb-{env}]
            APPI[Application Insights<br/>appi-smartkb-{env}]
            AG[Action Group<br/>ag-smartkb-slo-{env}]
            ALERTS[Metric Alerts<br/>- Chat Latency P95<br/>- API Availability<br/>- Dead Letters<br/>- HTTP 5xx<br/>- Queue Backlog]
        end
    end

    PLAN --> API_APP & ING_APP
    SQL_SVR --> SQL_DB
    STORAGE --> BLOB_CTR
    SB_NS --> SB_Q
    APPI --> LOG
    ALERTS --> AG

    API_APP -.->|MI: SQL Admin| SQL_DB
    API_APP -.->|MI: Blob Contributor| STORAGE
    API_APP -.->|MI: Search Contributor| SRCH
    API_APP -.->|MI: SB Data Sender| SB_Q
    API_APP -.->|MI: KV Secrets User| KV
    API_APP -.->|Telemetry| APPI

    ING_APP -.->|MI: Blob Contributor| STORAGE
    ING_APP -.->|MI: Search Contributor| SRCH
    ING_APP -.->|MI: SB Data Receiver| SB_Q
    ING_APP -.->|MI: KV Secrets User| KV
    ING_APP -.->|Telemetry| APPI

    CMK_ID -.->|MI: KV Crypto Officer| KV
```

---

## 3. RAG Pipeline (Chat Flow)

```mermaid
flowchart TB
    USER[User Query] --> CLASSIFY

    subgraph PreRetrieval["Stage 1: Pre-Retrieval"]
        CLASSIFY[Query Classification<br/>gpt-4o-mini]
        CLASSIFY --> |category, product_area,<br/>severity, filters| EMBED
        EMBED[Generate Embedding<br/>text-embedding-3-large<br/>1536 dimensions]
        CACHE{Embedding<br/>Cache Hit?}
        EMBED --> CACHE
        CACHE -->|Yes| RETRIEVE
        CACHE -->|No| OPENAI_EMB[OpenAI Embedding API]
        OPENAI_EMB --> RETRIEVE
    end

    subgraph Retrieval["Stage 2: Hybrid Retrieval (Parallel)"]
        RETRIEVE[Retrieval Request] --> EV_IDX & PAT_IDX

        subgraph EvidenceSearch["Evidence Index"]
            EV_IDX[Azure AI Search<br/>evidence-index]
            EV_IDX --> |BM25 + Vector| EV_RRF[Reciprocal Rank Fusion]
        end

        subgraph PatternSearch["Pattern Index"]
            PAT_IDX[Azure AI Search<br/>pattern-index]
            PAT_IDX --> |BM25 + Vector| PAT_RRF[Reciprocal Rank Fusion]
        end
    end

    subgraph Filtering["Stage 3: Security & Filtering"]
        EV_RRF & PAT_RRF --> ACL[ACL Security Trimming]
        ACL --> |Remove inaccessible| FUSE[Fused Ranking]
        FUSE --> |Trust boost, recency,<br/>diversity constraints| NOEV{Has Evidence?}
    end

    subgraph Generation["Stage 4: Response Generation"]
        NOEV -->|Yes| PII[PII Redaction]
        NOEV -->|No: < 3 results or<br/>scores < 0.3| NEXTSTEPS[Next Steps Only<br/>+ Escalation Signal]
        PII --> BUDGET[Token Budget<br/>Enforcement]
        BUDGET --> PROMPT[Assemble System Prompt<br/>+ Context + History]
        PROMPT --> LLM[OpenAI GPT-4o<br/>Structured Output]
    end

    subgraph PostProcess["Stage 5: Post-Processing"]
        LLM --> CONF[Confidence Blending<br/>Model + Retrieval Heuristic]
        CONF --> ESC{Escalation<br/>Signal?}
        ESC -->|Yes| DRAFT[Auto-Draft<br/>Escalation]
        ESC -->|No| TRACE
        DRAFT --> TRACE[Write Answer Trace<br/>+ Audit Event]
    end

    TRACE --> RESP[Chat Response<br/>answer, citations, confidence,<br/>next_steps, escalation]
    NEXTSTEPS --> RESP
```

---

## 4. Data Ingestion Pipeline

```mermaid
flowchart TB
    subgraph Triggers["Sync Triggers"]
        MANUAL[Admin: Sync Now]
        SCHED[Scheduled Sync<br/>NCrontab cron]
        WEBHOOK[Webhook Event<br/>ADO/SP/HubSpot/ClickUp]
    end

    MANUAL & SCHED & WEBHOOK --> PUBLISH[Publish SyncJobMessage<br/>to Service Bus]
    PUBLISH --> |TenantId, ConnectorId,<br/>SyncRunId, Checkpoint| SB_Q[Service Bus Queue<br/>ingestion-jobs]

    subgraph Worker["Ingestion Worker (BackgroundService)"]
        SB_Q --> RECEIVE[IngestionWorker<br/>Receives Message]
        RECEIVE --> PROC[SyncJobProcessor]

        PROC --> STATUS1[SyncRun → Running]
        STATUS1 --> RESOLVE[Resolve Connector Client<br/>+ Retrieve Secret from Key Vault]
        RESOLVE --> FETCH[Fetch Records<br/>via Connector Client]

        subgraph Loop["Pagination Loop"]
            FETCH --> NORM[Normalization Pipeline]

            subgraph NormPipeline["Normalize"]
                NORM --> CHUNK[Text Chunking<br/>512 tokens, 64 overlap]
                CHUNK --> ENRICH[Enrichment<br/>metadata, product area, tags]
                ENRICH --> DEDUP{Content Hash<br/>Changed?}
            end

            DEDUP -->|Yes| PERSIST[Persist to SQL<br/>EvidenceChunkEntity]
            DEDUP -->|No| SKIP[Skip: No Change]
            PERSIST --> INDEX[Index to Azure AI Search<br/>evidence-index]
            INDEX --> SNAP[Store Raw Snapshot<br/>Azure Blob Storage]
            SNAP --> MORE{More Pages?}
            SKIP --> MORE
            MORE -->|Yes| FETCH
        end

        MORE -->|No| STATUS2[SyncRun → Completed]
        STATUS2 --> AUDIT_EV[Emit Audit Event]
    end

    subgraph ErrorHandling["Error Handling"]
        PROC -->|Failure| RETRY[Retry up to 10x]
        RETRY -->|Exhausted| DLQ[Dead Letter Queue]
        DLQ --> ALERT[SLO Alert:<br/>Dead Letters > 10]
    end
```

---

## 5. Pattern Distillation & Governance

```mermaid
flowchart TB
    subgraph Discovery["Pattern Discovery"]
        SESSIONS[(Sessions with<br/>Positive Outcomes)]
        SESSIONS --> CANDIDATES[Find Candidates<br/>MinPositiveFeedback threshold]
        CANDIDATES --> REVIEW_LIST[Candidate List<br/>SessionId, Score, Confidence]
    end

    subgraph Distillation["Distillation"]
        REVIEW_LIST --> DISTILL[Extract Pattern<br/>problem, symptoms,<br/>diagnosis, resolution]
        DISTILL --> VALIDATE[Quality Validation<br/>CaseCardQualityValidator]
        VALIDATE -->|Pass| CREATE[Create CasePatternEntity<br/>TrustLevel = Draft]
        VALIDATE -->|Fail| REJECT[Reject Candidate]
        CREATE --> PAT_IDX[Index to<br/>pattern-index]
    end

    subgraph Governance["Governance Workflow"]
        CREATE --> QUEUE[Review Queue]
        QUEUE --> REVIEWED[Mark Reviewed<br/>by SupportLead]
        REVIEWED --> APPROVE{Decision}
        APPROVE -->|Approve| TRUSTED[TrustLevel → Approved<br/>Full retrieval weight]
        APPROVE -->|Deprecate| DEPRECATED[TrustLevel → Deprecated<br/>Removed from index]
    end

    subgraph Monitoring["Pattern Health"]
        TRUSTED --> USAGE[Track Usage<br/>via AnswerTrace citations]
        USAGE --> MAINT{Unused ><br/>threshold?}
        MAINT -->|Yes| TASK[Create Maintenance Task]
        TASK --> DEPRECATED
    end

    subgraph Contradiction["Contradiction Detection"]
        CREATE --> CONTRA[Detect Conflicts<br/>with existing patterns]
        CONTRA -->|Conflict| FLAG[Flag for Review]
    end
```

---

## 6. Security & Multi-Tenancy

```mermaid
flowchart TB
    subgraph Request["Incoming Request"]
        JWT[JWT Bearer Token<br/>from Entra ID]
    end

    JWT --> MW[TenantContextMiddleware]

    subgraph TenantExtraction["Tenant Context Extraction"]
        MW --> TID{tid claim<br/>present?}
        TID -->|No| DENY[403 Forbidden<br/>+ TenantMissing Audit]
        TID -->|Yes| CTX[Create TenantContext<br/>TenantId, UserId,<br/>CorrelationId, UserGroups]
    end

    CTX --> AUTH[PermissionAuthorizationHandler]

    subgraph RBAC["RBAC Enforcement"]
        AUTH --> ROLE{User has<br/>required role?}
        ROLE -->|No| FORBID[403 Forbidden]
        ROLE -->|Yes| ENDPOINT[Execute Endpoint]
    end

    subgraph DataIsolation["Data Isolation"]
        ENDPOINT --> EF[EF Core Query]
        EF --> FILTER[Global Query Filter<br/>WHERE TenantId = @tid<br/>AND DeletedAt IS NULL]
        FILTER --> DB[(Azure SQL)]
    end

    subgraph SearchIsolation["Search Isolation"]
        ENDPOINT --> SEARCH_Q[Search Query]
        SEARCH_Q --> ODATA[OData Filter<br/>tenant_id eq @tid]
        ODATA --> ACL_CHECK[ACL Trimming]

        subgraph ACLRules["ACL Rules"]
            ACL_CHECK --> VIS{Visibility?}
            VIS -->|Public/Internal| PASS[Include]
            VIS -->|Restricted| GROUP{User in<br/>allowed_groups?}
            GROUP -->|Yes| PASS
            GROUP -->|No| EXCLUDE[Exclude]
        end
    end

    subgraph AuditTrail["Audit Trail"]
        ENDPOINT --> AUDIT[Immutable AuditEvent<br/>EventType, ActorId,<br/>TenantId, CorrelationId]
        AUDIT --> IMMUTABLE[AuditImmutabilityInterceptor<br/>Prevents UPDATE/DELETE]
    end
```

---

## 7. RBAC Role-Permission Matrix

```mermaid
graph LR
    subgraph Roles
        R1[Admin]
        R2[SupportLead]
        R3[SupportAgent]
        R4[EngineeringViewer]
        R5[SecurityAuditor]
    end

    subgraph Permissions
        P1[chat:query]
        P2[chat:feedback]
        P3[chat:outcome]
        P4[connector:manage]
        P5[connector:sync]
        P6[pattern:approve]
        P7[pattern:deprecate]
        P8[audit:read]
        P9[audit:export]
        P10[privacy:manage]
        P11[report:read]
    end

    R1 --> P1 & P2 & P3 & P4 & P5 & P6 & P7 & P8 & P9 & P10 & P11
    R2 --> P1 & P2 & P3 & P6 & P7 & P11
    R3 --> P1 & P2 & P3
    R4 --> P1 & P11
    R5 --> P8 & P9 & P10
```

---

## 8. .NET Project Dependency Graph

```mermaid
graph BT
    CONTRACTS[SmartKb.Contracts<br/>Shared DTOs, Enums,<br/>Interfaces, Services,<br/>Connectors, Config]

    DATA[SmartKb.Data<br/>EF Core DbContext,<br/>43 Entities, 26+ Migrations,<br/>30+ Repository Services]

    API[SmartKb.Api<br/>Minimal API, 13 Endpoint Groups,<br/>Auth, Tenant Middleware,<br/>Webhooks, DI Setup]

    INGESTION[SmartKb.Ingestion<br/>IngestionWorker,<br/>SyncJobProcessor,<br/>ScheduledSyncService]

    EVAL[SmartKb.Eval<br/>EvalRunner,<br/>MetricCalculator,<br/>BaselineComparator]

    EVAL_CLI[SmartKb.Eval.Cli<br/>CLI Runner,<br/>HTTP Orchestrator Client,<br/>GitHub Actions Formatter]

    DATA --> CONTRACTS
    API --> CONTRACTS
    API --> DATA
    INGESTION --> CONTRACTS
    INGESTION --> DATA
    EVAL --> CONTRACTS
    EVAL_CLI --> EVAL

    subgraph Tests
        T1[SmartKb.Api.Tests]
        T2[SmartKb.Data.Tests]
        T3[SmartKb.Contracts.Tests]
        T4[SmartKb.Ingestion.Tests]
        T5[SmartKb.Eval.Tests]
    end

    T1 --> API
    T2 --> DATA
    T3 --> CONTRACTS
    T4 --> INGESTION
    T5 --> EVAL & EVAL_CLI
```

---

## 9. Frontend Component Hierarchy

```mermaid
graph TB
    APP[App.tsx<br/>BrowserRouter]
    APP --> EB[ErrorBoundary]
    EB --> AP[AuthProvider<br/>MSAL + Entra ID]
    AP --> ROUTES[Routes]

    ROUTES --> CHAT[ChatPage /]
    ROUTES --> ADM[AdminPage /admin]
    ROUTES --> PAT[PatternGovernancePage /patterns]
    ROUTES --> DIAG[DiagnosticsPage /diagnostics]
    ROUTES --> SYN[SynonymManagementPage /synonyms]
    ROUTES --> ROUT[RoutingAnalyticsPage /routing]
    ROUTES --> PLAY[PlaybooksPage /playbooks]
    ROUTES --> COST_P[CostControlsPage /cost]
    ROUTES --> PRIV_P[PrivacyAdminPage /privacy]
    ROUTES --> AUD[AuditCompliancePage /audit]
    ROUTES --> GOLD[GoldDatasetPage /gold-cases]

    subgraph ChatComponents["Chat Components"]
        CHAT --> SS[SessionSidebar]
        CHAT --> CT[ChatThread]
        CHAT --> MI[MessageInput]
        CHAT --> ED[EvidenceDrawer]
        CHAT --> EDM[EscalationDraftModal]
        CHAT --> OW[OutcomeWidget]
        CHAT --> RFP[RetrievalFilterPanel]
        CT --> CB[ConfidenceBadge]
        CT --> FW[FeedbackWidget]
    end

    subgraph AdminComponents["Admin Components"]
        ADM --> CL[ConnectorList]
        ADM --> CD[ConnectorDetail]
        ADM --> CCF[CreateConnectorForm]
        CD --> SCE[SourceConfigEditor]
        CD --> FME[FieldMappingEditor]
        CD --> SRH[SyncRunHistory]
    end

    subgraph PatternComponents["Pattern Components"]
        PAT --> PL[PatternList]
        PAT --> PDV[PatternDetailView]
    end
```

---

## 10. Data Model (Core Entities)

```mermaid
erDiagram
    TenantEntity ||--o{ ConnectorEntity : "has"
    TenantEntity ||--o{ SessionEntity : "has"
    TenantEntity ||--o{ CasePatternEntity : "has"
    TenantEntity ||--o{ EscalationRoutingRuleEntity : "has"
    TenantEntity ||--o{ AuditEventEntity : "has"
    TenantEntity ||--|| TenantRetrievalSettingsEntity : "configures"
    TenantEntity ||--|| TenantCostSettingsEntity : "configures"

    ConnectorEntity ||--o{ SyncRunEntity : "triggers"
    ConnectorEntity ||--o{ EvidenceChunkEntity : "produces"
    ConnectorEntity ||--o{ WebhookSubscriptionEntity : "registers"

    SyncRunEntity ||--o{ RawContentSnapshotEntity : "stores"

    SessionEntity ||--o{ MessageEntity : "contains"
    SessionEntity ||--o{ OutcomeEventEntity : "resolves"

    MessageEntity ||--o{ FeedbackEntity : "receives"
    MessageEntity ||--o{ AnswerTraceEntity : "traces"

    CasePatternEntity ||--o{ PatternVersionHistoryEntity : "versions"
    CasePatternEntity ||--o{ PatternContradictionEntity : "conflicts"
    CasePatternEntity ||--o{ PatternMaintenanceTaskEntity : "maintains"

    EvidenceChunkEntity {
        string ChunkId PK
        string EvidenceId
        string TenantId FK
        string ConnectorId FK
        int ChunkIndex
        string ChunkText
        string ChunkContext
        string SourceSystem
        string SourceType
        string ProductArea
        string Tags
        string Visibility
        string AllowedGroups
        string ContentHash
        int EnrichmentVersion
        datetime CreatedAt
        datetime UpdatedAt
    }

    CasePatternEntity {
        string PatternId PK
        string TenantId FK
        string Title
        string ProblemStatement
        string RootCause
        string Symptoms
        string DiagnosisSteps
        string ResolutionSteps
        string VerificationSteps
        string Workaround
        string EscalationCriteria
        float Confidence
        string TrustLevel
        int Version
        string SupersedesPatternId
        float QualityScore
        datetime CreatedAt
        datetime UpdatedAt
    }

    SessionEntity {
        string SessionId PK
        string TenantId FK
        string UserId
        datetime CreatedAt
        datetime ExpiresAt
    }

    ConnectorEntity {
        string ConnectorId PK
        string TenantId FK
        string Name
        string ConnectorType
        string Status
        string AuthType
        string KeyVaultSecretName
        string SourceConfig
        string FieldMapping
        string ScheduleCron
        datetime DeletedAt
    }
```

---

## 11. CI/CD Pipeline

```mermaid
flowchart TB
    subgraph Triggers
        PR[Pull Request to main]
        PUSH[Push to main]
        MANUAL[Manual Dispatch]
        WEEKLY[Weekly Schedule]
        NIGHTLY[Nightly Schedule]
    end

    PR & PUSH --> CI[CI Pipeline]
    MANUAL --> DEPLOY[Deploy Pipeline]
    WEEKLY --> DRIFT[Drift Detection]
    NIGHTLY --> EVAL_N[Nightly Eval]

    subgraph CIPipeline["CI Pipeline (ci.yml)"]
        CI --> DOTNET_CI[.NET: restore → build → test<br/>~2922 backend tests]
        CI --> FE_CI[Frontend: npm ci → lint → test → build<br/>~499 frontend tests]
    end

    subgraph DeployPipeline["Deploy Pipeline (deploy.yml)"]
        DEPLOY --> CI_CHECK[CI Check Gate]
        CI_CHECK --> INFRA_PLAN[Terraform Plan]
        INFRA_PLAN --> |has changes| INFRA_APPLY[Terraform Apply<br/>requires approval]
        CI_CHECK --> DEPLOY_API[Deploy API<br/>az webapp deploy]
        CI_CHECK --> DEPLOY_ING[Deploy Ingestion<br/>az webapp deploy]
        CI_CHECK --> DEPLOY_FE[Deploy Frontend<br/>Static Web App]
        DEPLOY_API & DEPLOY_ING & DEPLOY_FE --> SMOKE[Smoke Test<br/>health check + eval]
    end

    subgraph DriftPipeline["Drift Detection (weekly)"]
        DRIFT --> PARITY[Terraform/ARM<br/>Parity Check]
        DRIFT --> TF_PLAN[Terraform Plan<br/>-detailed-exitcode]
        TF_PLAN -->|Drift detected| ISSUE[Create GitHub Issue]
    end

    subgraph InfraValidation["Infra Validation (on infra/ changes)"]
        PR --> TF_FMT[terraform fmt -check]
        PR --> TF_VAL[terraform validate]
        PR --> ARM_VAL[ARM JSON validation]
        PR --> PARITY_CHK[Parity checker]
    end
```

---

## 12. Two-Store Search Architecture

```mermaid
graph TB
    subgraph EvidenceIndex["Evidence Index (evidence-index)"]
        EI_SEARCH[Searchable Fields<br/>chunk_text, chunk_context, title<br/>Analyzer: en.microsoft]
        EI_VECTOR[Vector Field<br/>embedding_vector<br/>1536-dim HNSW cosine]
        EI_FILTER[Filterable Fields<br/>tenant_id, evidence_id,<br/>source_system, source_type,<br/>product_area, tags,<br/>status, updated_at]
        EI_ACL[ACL Fields<br/>visibility, allowed_groups,<br/>access_label]
    end

    subgraph PatternIndex["Pattern Index (pattern-index)"]
        PI_SEARCH[Searchable Fields<br/>problem_statement, resolution,<br/>root_cause, symptoms,<br/>workaround, verification_steps,<br/>escalation_playbook]
        PI_VECTOR[Vector Field<br/>embedding_vector<br/>1536-dim HNSW cosine]
        PI_FILTER[Filterable Fields<br/>tenant_id, trust_level,<br/>product_area, updated_at]
        PI_ACL[ACL Fields<br/>visibility, allowed_groups,<br/>access_label]
    end

    QUERY[User Query] --> HYBRID_E & HYBRID_P

    HYBRID_E[Hybrid Search<br/>BM25 + Vector<br/>TopK=20, RRF K=60] --> EvidenceIndex
    HYBRID_P[Hybrid Search<br/>BM25 + Vector<br/>PatternTopK] --> PatternIndex

    EvidenceIndex --> FUSION[FusedRetrievalService]
    PatternIndex --> FUSION

    FUSION --> |Trust boost: Approved > Verified > Draft<br/>Recency boost<br/>Diversity constraints<br/>ACL trimming| RESULTS[Merged Ranked Results]
```

---

## 13. Connector Data Flow

```mermaid
flowchart LR
    subgraph Sources["External Data Sources"]
        ADO[Azure DevOps<br/>Work Items + Wikis]
        SP[SharePoint<br/>Documents + Lists]
        HB[HubSpot<br/>Tickets + Deals]
        CU[ClickUp<br/>Tasks + Lists]
    end

    subgraph Connectors["Connector Clients"]
        ADO --> ADO_C[AzureDevOpsConnectorClient]
        SP --> SP_C[SharePointConnectorClient]
        HB --> HB_C[HubSpotConnectorClient]
        CU --> CU_C[ClickUpConnectorClient]
    end

    subgraph WebhookMgrs["Webhook Managers"]
        ADO_W[AdoWebhookManager<br/>Service Hooks]
        SP_W[SharePointWebhookManager<br/>Graph Notifications]
        HB_W[HubSpotWebhookManager]
        CU_W[ClickUpWebhookManager<br/>HMAC Verification]
    end

    subgraph Pipeline["Normalization Pipeline"]
        FETCH[Fetch CanonicalRecords] --> TEXT[Text Extraction<br/>PDF, DOCX, HTML]
        TEXT --> CHUNK[Chunking<br/>512 tokens / 64 overlap]
        CHUNK --> ENRICH[Enrichment<br/>product_area, tags,<br/>routing_tags, ACL]
        ENRICH --> EMBED[Embedding<br/>text-embedding-3-large]
    end

    subgraph Storage["Persistence"]
        SQL[(Azure SQL<br/>EvidenceChunkEntity)]
        SEARCH_IDX[Azure AI Search<br/>evidence-index]
        BLOB_ST[Azure Blob<br/>raw-content]
    end

    ADO_C & SP_C & HB_C & CU_C --> FETCH
    EMBED --> SQL & SEARCH_IDX & BLOB_ST
    ADO_W & SP_W & HB_W & CU_W -.->|Event-driven trigger| FETCH
```

---

## 14. Escalation Flow

```mermaid
flowchart TB
    CHAT_RESP[Chat Response] --> ESC_CHECK{Escalation<br/>Recommended?}
    ESC_CHECK -->|No| DONE[Display Answer]
    ESC_CHECK -->|Yes| BANNER[Show Escalation Banner<br/>Target Team + Reason]

    BANNER --> AGENT_ACTION{Agent Action}
    AGENT_ACTION -->|Create Draft| MODAL[EscalationDraftModal]

    MODAL --> PREFILL[Auto-Fill from Chat:<br/>Summary, Repro Steps,<br/>Suspected Component,<br/>Severity, Evidence Links]

    PREFILL --> EDIT[Agent Reviews/Edits]
    EDIT --> APPROVE[Approve Draft]
    APPROVE --> TARGET{Target System}

    TARGET -->|Azure DevOps| ADO_CREATE[Create Work Item<br/>via ADO Connector]
    TARGET -->|ClickUp| CU_CREATE[Create Task<br/>via ClickUp Connector]

    subgraph RoutingLogic["Routing Rule Resolution"]
        RULE_MATCH[Match ProductArea<br/>to EscalationRoutingRule]
        RULE_MATCH --> FIRST{Rule Found?}
        FIRST -->|Yes| TEAM[Use Rule's Target Team<br/>+ Confidence Threshold]
        FIRST -->|No| FALLBACK[Fallback: Support Team]
    end

    ESC_CHECK -.-> RULE_MATCH
```

---

## 15. Observability Stack

```mermaid
graph TB
    subgraph Apps["Application Layer"]
        API[SmartKb.Api]
        ING[SmartKb.Ingestion]
    end

    subgraph OTel["OpenTelemetry Instrumentation"]
        TRACES[Distributed Traces<br/>HTTP, SQL, Service Bus]
        METRICS[Custom Metrics<br/>chat_latency, retrieval_duration,<br/>tokens_used, sync_records_processed]
        LOGS[Structured Logs<br/>ILogger with scopes]
        CORR[Correlation IDs<br/>X-Correlation-Id / W3C TraceContext]
    end

    subgraph AuditSystem["Audit System"]
        AUDIT_WRITE[SqlAuditEventWriter<br/>Immutable Events]
        AUDIT_TYPES[Event Types:<br/>ConnectorCreated, SyncCompleted,<br/>PatternApproved, ChatQuery,<br/>EscalationCreated, TenantMissing,<br/>DataDeletionRequested, ...]
    end

    subgraph Azure["Azure Monitor"]
        APPI[Application Insights]
        LOG_A[Log Analytics<br/>KQL Queries]
        ALERTS[Metric Alerts]
        ACTION[Action Group<br/>Notifications]
    end

    subgraph SLOs["SLO Thresholds"]
        SLO1[Chat Latency P95 < 8s]
        SLO2[API Availability >= 99.5%]
        SLO3[Dead Letters <= 10]
        SLO4[HTTP 5xx <= 5/5min]
        SLO5[Queue Backlog <= 100]
    end

    API & ING --> TRACES & METRICS & LOGS & CORR
    API & ING --> AUDIT_WRITE
    TRACES & METRICS & LOGS --> APPI
    APPI --> LOG_A
    APPI --> ALERTS
    ALERTS --> ACTION
    SLO1 & SLO2 & SLO3 & SLO4 & SLO5 --> ALERTS
```

---

## 16. Environment Comparison

| Resource | Dev | Staging | Prod |
|----------|-----|---------|------|
| App Service Plan | B1 | P1v3 | P1v3 |
| SQL Database | Basic | S1 | S1 |
| Azure AI Search | basic | standard | standard |
| Service Bus | Basic | Standard | Standard |
| Static Web App | Free | Standard | Standard |
| CMK Encryption | Off | Optional | Optional |
| Always-On | Off | On | On |
| SLO Alerts | Disabled | Enabled | Enabled |
| Purge Protection | Off | Off | Auto-enabled |
| Terraform State | smartkb-dev.tfstate | smartkb-staging.tfstate | smartkb-prod.tfstate |
| Deploy Approval | No | No | Required |

---

## 17. Test Coverage

| Layer | Project | Tests | Focus |
|-------|---------|-------|-------|
| API | SmartKb.Api.Tests | ~900+ | Endpoint integration, auth, tenant isolation, RBAC |
| Data | SmartKb.Data.Tests | ~800+ | Repository services, EF Core, migrations |
| Contracts | SmartKb.Contracts.Tests | ~700+ | DTOs, services, connectors, search, enrichment |
| Ingestion | SmartKb.Ingestion.Tests | ~200+ | SyncJobProcessor, worker lifecycle, scheduling |
| Eval | SmartKb.Eval.Tests | ~100+ | Metrics, baseline comparison, CLI runner |
| Frontend | frontend (Vitest) | ~499 | Component rendering, API client, interactions |
| **Total** | | **~3421** | |

---

## 18. Key Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| RAG Architecture | Two-store (Evidence + Pattern) | Evidence for auditability; Patterns for reusability |
| Search Strategy | Hybrid BM25 + Vector + RRF | Best recall across keyword and semantic queries |
| Embedding Model | text-embedding-3-large (1536d) | High quality, good cost/performance balance |
| LLM | GPT-4o (Structured Outputs) | Reliable JSON schema enforcement |
| Auth | Entra ID + MSAL | Enterprise SSO, tenant isolation via `tid` claim |
| IaC | Terraform + ARM (dual) | Terraform for state; ARM for Azure-native deployment |
| Messaging | Azure Service Bus | Dead-lettering, retry policies, managed identity |
| Multi-tenancy | Row-level via EF Core global filters | Simple, secure, consistent enforcement |
| Secret Management | Azure Key Vault + Managed Identity | No secrets in code or config |
| Frontend | React + Vite (no state library) | Simple local state; no Redux overhead needed |
