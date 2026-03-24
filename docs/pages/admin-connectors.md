# Admin — Connectors Page

**Route:** `/admin`
**Required Role:** Admin
**Component:** `AdminPage.tsx`

Manage data source connectors that feed the Evidence Store. Configure authentication, field mappings, and sync schedules for each external system.

## Layout

The page has three views: List → Detail → Create.

### List View

| Column | Description |
|--------|-------------|
| Name | Connector display name |
| Type | Azure DevOps, SharePoint, HubSpot, or ClickUp |
| Status | Enabled / Disabled |
| Auth Type | OAuth, PAT, API Key, etc. |
| Last Sync | Status and timestamp of most recent sync |
| Records | Number of records indexed |
| Updated | Last configuration change |

### Detail View

Clicking a connector opens its detail view:

- **Configuration**: name, type, base URL, auth settings
- **Field mapping editor**: map source fields to the SmartKB canonical schema
- **Sync schedule**: cron expression for automatic syncing
- **Sync history**: timeline of past sync runs with status, record counts, errors
- **Actions**: enable/disable, sync now, test credentials, delete

### Create Connector Form

Multi-step form:

1. **Select type**: Azure DevOps, SharePoint, HubSpot, or ClickUp
2. **Configure auth**: enter credentials (stored in Azure Key Vault)
3. **Configure source scope**: structured form tailored to the selected connector type (see below). An "Edit as JSON" toggle is available for advanced users.
4. **Review and create**

### Source Configuration Forms

Each connector type presents a guided form instead of raw JSON:

| Type | Fields |
|------|--------|
| **Azure DevOps** | Organization URL, projects, ingest work items/wiki toggles, work item type filter, area path filter, batch size |
| **SharePoint** | Site URL, Entra ID tenant ID, client ID, drive IDs, ingest document libraries toggle, include extensions, exclude folders, batch size |
| **HubSpot** | Portal ID, object types (default: tickets), pipelines, custom properties, batch size |
| **ClickUp** | Workspace ID, space/folder/list IDs, ingest tasks/docs toggles, task statuses, batch size |

- **Tag fields** (projects, drive IDs, etc.) accept comma-separated values.
- The form populates from existing configuration when editing a connector.
- Invalid or legacy JSON gracefully falls back to defaults.

## Supported Connector Types

| Type | Auth Methods | Data Source |
|------|-------------|-------------|
| **Azure DevOps** | PAT, OAuth | Work items, wiki pages |
| **SharePoint** | Microsoft Graph (OAuth) | Documents, list items |
| **HubSpot** | API Key, OAuth | Tickets, knowledge base articles |
| **ClickUp** | API Key, OAuth | Tasks, docs |

## OAuth Authorization Code Flow

Connectors configured with `AuthType: OAuth` support a full authorization code flow:

1. Admin creates connector with `AuthType: OAuth` and sets `KeyVaultSecretName` pointing to a Key Vault secret containing `{"client_id":"...","client_secret":"..."}`.
2. Admin calls `GET /api/admin/connectors/{id}/oauth/authorize` to get the provider's authorization URL.
3. Admin is redirected to the provider's consent page (HubSpot, ClickUp, ADO, or SharePoint).
4. After granting access, the provider redirects back to `GET /api/admin/connectors/{id}/oauth/callback` with an authorization code.
5. The system exchanges the code for access and refresh tokens, which are stored in Key Vault.
6. During sync, the system automatically refreshes expired tokens using the refresh token.

OAuth configuration is set per connector type via optional fields in the source config JSON (`oAuthClientId`, `oAuthScopes`).

### Preview and Retrieval Testing

The detail view includes two diagnostic tools for verifying connector data quality:

**Preview** — Click "Preview" to fetch a sample of normalized records from the connector. The preview table highlights missing required fields (Title, TextContent, SourceType) with red labels, helping admins identify field mapping issues before running a full sync. A field coverage summary shows which canonical fields are mapped and flags missing required mappings.

**Retrieval Test** — Enter a search query in the "Test Retrieval" input to run a scoped search against only this connector's indexed chunks. Results show matching chunk titles, text snippets, source types, product areas, scores, and timestamps. A summary line reports how many results were found out of the connector's total chunk count. This helps verify that ingested content is searchable and relevant before exposing it to agents.

## Navigation

Header links to: Diagnostics, Patterns, Synonyms, Chat

## API Endpoints Used

- `GET /api/admin/connectors` — list connectors
- `POST /api/admin/connectors` — create connector
- `GET /api/admin/connectors/{id}` — get details
- `PUT /api/admin/connectors/{id}` — update
- `DELETE /api/admin/connectors/{id}` — delete
- `POST /api/admin/connectors/{id}/enable` — enable
- `POST /api/admin/connectors/{id}/disable` — disable
- `POST /api/admin/connectors/{id}/sync-now` — trigger sync
- `POST /api/admin/connectors/{id}/test` — test credentials
- `POST /api/admin/connectors/{id}/validate-mapping` — validate field mapping
- `POST /api/admin/connectors/{id}/preview` — preview normalized records with field coverage analysis
- `POST /api/admin/connectors/{id}/preview-retrieval` — test retrieval against connector chunks
- `GET /api/admin/connectors/{id}/sync-runs` — sync history
- `GET /api/admin/connectors/{id}/oauth/authorize` — get OAuth authorization URL
- `GET /api/admin/connectors/{id}/oauth/callback` — handle OAuth callback (code exchange)
