# Getting Started

Local development setup for Smart KB.

## Prerequisites

- .NET 10 SDK
- Node.js 20+
- Azure CLI (for Key Vault / managed identity access)
- Git

## Backend Setup

```bash
# Install dependencies and build
dotnet restore
dotnet build

# Run tests
dotnet test

# Start the API (localhost:5000)
dotnet run --project src/SmartKb.Api/SmartKb.Api.csproj

# Start the Ingestion Worker (separate terminal)
dotnet run --project src/SmartKb.Ingestion/SmartKb.Ingestion.csproj
```

### Configuration

Backend configuration lives in `src/SmartKb.Api/appsettings.json` with development overrides in `appsettings.Development.json`.

**Key settings:**

| Setting | Description |
|---------|-------------|
| `ConnectionStrings:SmartKbDb` | Azure SQL connection string (Active Directory Default auth) |
| `AzureAd:*` | Entra ID tenant, client ID, audience |
| `OpenAi:ApiKey` | OpenAI API key (loaded from Key Vault in production) |
| `OpenAi:Model` | Model name (default: `gpt-4o`) |
| `ServiceBus:FullyQualifiedNamespace` | Service Bus namespace for ingestion jobs |
| `KeyVault:VaultUri` | Azure Key Vault URI for connector secrets |
| `SearchService:Endpoint` | Azure AI Search endpoint |
| `BlobStorage:ServiceUri` | Blob Storage for raw content |

When Azure services are not configured, the API uses in-memory fallback implementations for local development.

## Frontend Setup

```bash
cd frontend

# Install dependencies
npm ci

# Run tests
npm run test

# Start dev server (localhost:3000)
npm run dev

# Lint
npm run lint

# Production build
npm run build
```

### Environment Variables

Create `frontend/.env.local` (not committed):

```bash
VITE_ENTRA_CLIENT_ID=<your-entra-app-client-id>
VITE_ENTRA_AUTHORITY=https://login.microsoftonline.com/<tenant-id>
VITE_API_BASE_URL=http://localhost:5000
```

The Vite dev server proxies `/api/*` requests to `http://localhost:5000`.

## Entra ID (Azure AD) Setup

Authentication uses Entra ID App Roles. You must configure an App Registration and assign roles to users before the app will grant access to protected pages (Admin, Diagnostics, etc.).

### 1. Create an App Registration

1. Go to **Azure Portal** > **Microsoft Entra ID** > **App registrations** > **New registration**
2. Name: `Smart KB` (or your preferred name)
3. Redirect URI: `http://localhost:5173` for local dev (add your production URL later)
4. After creation, note the **Application (client) ID** and **Directory (tenant) ID**

### 2. Configure the Application ID URI

1. In your App Registration, go to **Expose an API**
2. Set the Application ID URI to `api://smart-kb`
3. Add a scope: `api://smart-kb/.default`

### 3. Define App Roles

In your App Registration, go to **App roles** and create the following roles. The **Value** must match exactly as shown (these correspond to the `AppRole` enum in `src/SmartKb.Contracts/Enums/AppRole.cs`):

| Display Name | Value | Description | Allowed Member Types |
|---|---|---|---|
| Admin | `Admin` | Full access to all features including tenant management, connectors, privacy, and audit | Users/Groups |
| Support Lead | `SupportLead` | Chat, session history, pattern governance, and reports | Users/Groups |
| Support Agent | `SupportAgent` | Chat, feedback, and own session history | Users/Groups |
| Engineering Viewer | `EngineeringViewer` | Read-only access to reports and patterns | Users/Groups |
| Security Auditor | `SecurityAuditor` | Audit log access and export, reports | Users/Groups |

### 4. Assign Roles to Users

1. Go to **Microsoft Entra ID** > **Enterprise applications** (not App registrations)
2. Find and select your app
3. Go to **Users and groups** > **Add user/group**
4. Select your user, then select the desired role (e.g. `Admin`), and click **Assign**

> **Note:** Roles are *defined* in **App registrations** but *assigned* in **Enterprise applications**.

### 5. Configure the App

**Backend** (`src/SmartKb.Api/appsettings.Development.json`):

```json
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "<your-tenant-id>",
    "ClientId": "<your-client-id>",
    "Audience": "api://<your-client-id>"
  }
}
```

**Frontend** (`frontend/.env.local`):

```bash
VITE_ENTRA_CLIENT_ID=<your-client-id>
VITE_ENTRA_AUTHORITY=https://login.microsoftonline.com/<your-tenant-id>
```

### 6. Verify

After signing in, you can verify your token contains the correct roles by pasting your access token at https://jwt.ms. Look for the `roles` claim:

```json
{
  "roles": ["Admin"]
}
```

If roles are missing, sign out and sign back in to refresh the token.

### Role-Permission Matrix

Each role grants a specific set of permissions. See `src/SmartKb.Contracts/RolePermissions.cs` for the full matrix.

| Role | Key Permissions |
|------|----------------|
| **Admin** | All permissions (connectors, sync, patterns, audit, tenant, privacy, chat, reports) |
| **SupportLead** | Chat, feedback, team sessions, pattern governance, reports |
| **SupportAgent** | Chat, feedback, own sessions |
| **EngineeringViewer** | Reports (read-only), patterns (read-only) |
| **SecurityAuditor** | Audit logs (read + export), reports |

### Running Without Entra ID

If Entra ID is not configured (`AzureAd:ClientId` is empty or a placeholder), the backend falls back to unauthenticated mode in Development. In this mode, all API endpoints requiring authentication will return **401 Unauthorized**. Protected frontend pages (Admin, Diagnostics, etc.) will show "Access denied" because no roles are available.

To develop locally with full functionality, configure Entra ID as described above.

## Full CI-Equivalent Build

```bash
dotnet restore && dotnet build && dotnet test
npm ci --prefix frontend && npm run build --prefix frontend
```

## Running the Eval CLI

The evaluation CLI runs gold-dataset tests against the API:

```bash
# Smoke test (5 cases, offline validation)
dotnet run --project src/SmartKb.Eval.Cli/SmartKb.Eval.Cli.csproj -- \
  --mode smoke --smoke-count 5

# Full eval against live API
dotnet run --project src/SmartKb.Eval.Cli/SmartKb.Eval.Cli.csproj -- \
  --mode full --api-url https://app-smartkb-api-dev.azurewebsites.net \
  --api-token <token>
```

## Troubleshooting

| Issue | Resolution |
|-------|------------|
| SQL connection fails | Check connection string and ensure Azure AD auth is configured, or use local SQL Server |
| Key Vault access denied | Run `az login` and verify your identity has Key Vault Secrets User role |
| Frontend MSAL login fails | Verify `VITE_ENTRA_CLIENT_ID` matches your Entra ID app registration |
| Service Bus timeout | Verify namespace exists and your identity has Data Sender/Receiver roles |
| Search returns 403 | Check managed identity or API key configuration |
