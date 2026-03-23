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
