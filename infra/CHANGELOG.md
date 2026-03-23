# Infrastructure Changelog

All notable infrastructure changes to the Smart KB project are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
Version numbers use [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## Convention

- **MAJOR**: Breaking changes that require manual migration or could cause downtime (e.g., resource renames, SKU downgrades, removed resources).
- **MINOR**: New resources, new role assignments, new app settings, or new outputs — backward-compatible additions.
- **PATCH**: Tag changes, default value tweaks, documentation-only changes, cosmetic fixes.

> **Rule**: Every PR that modifies files under `infra/` MUST add an entry here and bump the version in
> `infra/terraform/variables.tf` (`infra_version` default), `infra/arm/main.json` (`metadata.infraVersion` + `contentVersion`).

---

## [1.6.0] — 2026-03-23

### Added
- `infra_version` variable in Terraform with semver validation and `infra_version` tag on all resources (P3-015).
- `metadata.infraVersion` field and updated `contentVersion` in ARM template (P3-015).
- `infra_version` tag in ARM `commonTags` variable (P3-015).
- This changelog file (`infra/CHANGELOG.md`) to track infrastructure template versions (P3-015).
- Version parity check in `check_parity.py` to verify Terraform and ARM version alignment (P3-015).
- `infra_version` output in Terraform `outputs.tf` (P3-015).

## [1.5.0] — 2026-03-22

### Added
- Azure Static Web App resource (`stapp-smartkb-{env}`) in Terraform and ARM (P3-036).
- `static_web_app_sku` variable (Free for dev, Standard for staging/prod).
- Parity checker mapping for `azurerm_static_web_app` ↔ `Microsoft.Web/staticSites`.

## [1.4.0] — 2026-03-20

### Fixed
- ARM `availabilityThresholdPercent` parameter type from `int`/`99` to `string`/`"99.5"` with `float()` conversion, matching Terraform default `99.5` (P3-037).

### Added
- `SearchService__Endpoint` and `BlobStorage__ServiceUri` app settings on both App Services.
- `Search Index Data Contributor`, `Search Service Contributor`, and `Storage Blob Data Contributor` RBAC role assignments for API and Ingestion App Services.

## [1.3.0] — 2026-03-17

### Added
- Terraform remote state backend (`azurerm` with Azure AD auth) and bootstrap script (P1-012).
- ARM parity template for Terraform state storage account (`infra/arm/tfstate-backend.json`).
- Backend config files per environment (`backend.dev.hcl`, `backend.staging.hcl`, `backend.prod.hcl`).

## [1.2.0] — 2026-03-16

### Fixed
- Added `https_only = true` to both `azurerm_linux_web_app.api` and `azurerm_linux_web_app.ingestion` in Terraform, closing security drift with ARM (BUG-004).

### Added
- SLO metric alert resources in Terraform and ARM: chat latency P95, availability, dead-letter count, HTTP 5xx, queue backlog (P0-022).
- Alert threshold variables (`chat_latency_p95_threshold_ms`, `availability_threshold_percent`, `dead_letter_threshold`, `http_5xx_threshold`, `queue_backlog_threshold`).

## [1.1.0] — 2026-03-15

### Added
- Ingestion Worker App Service (`app-smartkb-ingestion-{env}`) in Terraform and ARM (TECH-001).
- Managed Identity with Key Vault Secrets User and Service Bus Data Receiver roles for Ingestion App Service.
- `Service Bus Data Sender` role for API App Service (TECH-002).
- `ServiceBus__FullyQualifiedNamespace` app setting on both App Services for Managed Identity auth (TECH-002).

### Fixed
- ARM template: added `Microsoft.Sql/servers/administrators/ActiveDirectory` resource matching Terraform `azuread_administrator` block (BUG-002).

## [1.0.0] — 2026-03-14

### Added
- Initial IaC baseline: Resource Group, App Service Plan, API Web App, SQL Server + Database, Storage Account + Blob Container, Service Bus Namespace + Queue, Key Vault, Azure AI Search, Log Analytics Workspace, Application Insights (P0-005A).
- Terraform modular layout (`main.tf`, `variables.tf`, `outputs.tf`, per-service `.tf` files).
- ARM template (`main.json`) with environment parameter files (`parameters.dev.json`, `parameters.staging.json`, `parameters.prod.json`).
- CI validation workflow for `terraform fmt`/`validate` and ARM JSON structure checks (P0-005B).
- Terraform ↔ ARM parity checker script (`check_parity.py`) with property-level validation (P1-010).
