# Deployment

How to deploy Smart KB to Azure (dev / staging / prod environments).

## Environments

| Environment | API SKU | SQL SKU | Search SKU | Static Web App | Purpose |
|-------------|---------|---------|------------|----------------|---------|
| `dev` | B1 | Basic | basic | Free | Development and testing |
| `staging` | S1 | S0 | basic | Standard | Pre-production validation |
| `prod` | P1v3 | S1 | standard | Standard | Production |

## Infrastructure Provisioning

### 1. Bootstrap Terraform State Backend

Run once per Azure subscription:

```bash
./infra/scripts/bootstrap-tfstate.sh --location eastus
```

This creates the `rg-smartkb-tfstate` resource group with a storage account for Terraform remote state.

### 2. Provision with Terraform

```bash
cd infra/terraform

# Initialize with remote state
terraform init -backend-config=backend.dev.hcl

# Plan (review changes)
terraform plan -var-file=dev.tfvars -out=deploy.tfplan

# Apply
terraform apply deploy.tfplan
```

Replace `dev` with `staging` or `prod` as needed.

### 3. ARM Template Deployment (Alternative)

```bash
# Validate
az deployment group validate \
  --resource-group rg-smartkb-dev \
  --template-file infra/arm/main.json \
  --parameters @infra/arm/parameters.dev.json

# Deploy
az deployment group create \
  --resource-group rg-smartkb-dev \
  --template-file infra/arm/main.json \
  --parameters @infra/arm/parameters.dev.json
```

## CI/CD Pipelines

All pipelines are in `.github/workflows/`.

### CI (`ci.yml`)

Runs on every PR and push to main:

- **Backend**: `dotnet restore` → `dotnet build` → `dotnet test` (all ~1800 tests)
- **Frontend**: `npm ci` → `npm run lint` → `npm run test` → `npm run build`

### Infrastructure Validation (`infra-validate.yml`)

Runs on changes to `infra/**`:

- Terraform format + validate
- ARM JSON structure validation
- Terraform ↔ ARM parity check (`infra/scripts/check_parity.py`)

### Deploy (`deploy.yml`)

Manual trigger (`workflow_dispatch`) with inputs:

| Input | Options | Default |
|-------|---------|---------|
| `environment` | dev, staging, prod | required |
| `deploy_infra` | true/false | true |
| `deploy_backend` | true/false | true |
| `deploy_frontend` | true/false | true |

**Pipeline steps:**

1. CI checks (all tests must pass)
2. Terraform plan (review infrastructure changes)
3. Terraform apply (if changes detected)
4. Deploy API to `app-smartkb-api-{env}`
5. Deploy Ingestion Worker to `app-smartkb-ingestion-{env}`
6. Deploy React app to `stapp-smartkb-{env}` (Azure Static Web App)
7. Smoke test (health check + eval CLI with 5 cases)

### Eval Pipelines

- **`eval-nightly.yml`**: 30-case smoke eval at 02:00 UTC daily
- **`eval-weekly.yml`**: 50-case full eval at 02:00 UTC Sunday

## GitHub Secrets Required

| Secret | Description |
|--------|-------------|
| `ARM_CLIENT_ID` | Service principal client ID |
| `ARM_CLIENT_SECRET` | Service principal secret |
| `ARM_SUBSCRIPTION_ID` | Azure subscription ID |
| `ARM_TENANT_ID` | Entra tenant ID |
| `SQL_ADMIN_PASSWORD` | SQL Server admin password |
| `SWA_DEPLOYMENT_TOKEN` | Static Web App deployment token |

## GitHub Variables (Per-Environment)

| Variable | Example |
|----------|---------|
| `API_BASE_URL` | `https://app-smartkb-api-dev.azurewebsites.net` |
| `ENTRA_CLIENT_ID` | Your Entra ID app registration client ID |
| `ENTRA_AUTHORITY` | `https://login.microsoftonline.com/{tenant-id}` |

## Manual Deployment

### Backend

```bash
# Publish
dotnet publish src/SmartKb.Api/SmartKb.Api.csproj -c Release -o ./publish/api
dotnet publish src/SmartKb.Ingestion/SmartKb.Ingestion.csproj -c Release -o ./publish/ingestion

# Deploy via Azure CLI
az webapp deployment source config-zip \
  --resource-group rg-smartkb-dev \
  --name app-smartkb-api-dev \
  --src-path publish/api.zip
```

### Frontend

```bash
cd frontend
npm ci && npm run build    # Produces dist/
# Deploy via SWA CLI or GitHub Actions
```

## Post-Deployment Verification

```bash
# Health check
curl https://app-smartkb-api-dev.azurewebsites.net/api/health

# App Service logs
az webapp log tail --resource-group rg-smartkb-dev --name app-smartkb-api-dev

# Resource status
az resource list --resource-group rg-smartkb-dev --output table
```
