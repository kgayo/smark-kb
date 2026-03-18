#!/usr/bin/env bash
# bootstrap-tfstate.sh — Create Azure Storage resources for Terraform remote state.
#
# This script is idempotent: safe to re-run. It provisions:
#   - Resource group:    rg-smartkb-tfstate
#   - Storage account:   stsmartkbtfstate
#   - Blob container:    tfstate
#
# The storage account uses:
#   - TLS 1.2 minimum
#   - Blob versioning for state history
#   - Soft-delete (14 days) for recovery
#   - Azure AD auth (no storage account keys needed when RBAC is configured)
#
# Usage:
#   ./bootstrap-tfstate.sh                    # uses default location (eastus)
#   ./bootstrap-tfstate.sh --location westus2 # override location
#
# Prerequisites:
#   - Azure CLI logged in (az login)
#   - Sufficient permissions to create resource groups and storage accounts

set -euo pipefail

RESOURCE_GROUP="rg-smartkb-tfstate"
STORAGE_ACCOUNT="stsmartkbtfstate"
CONTAINER_NAME="tfstate"
LOCATION="eastus"

# Parse arguments
while [[ $# -gt 0 ]]; do
  case "$1" in
    --location)
      LOCATION="$2"
      shift 2
      ;;
    --help)
      echo "Usage: $0 [--location <azure-region>]"
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 1
      ;;
  esac
done

echo "=== Terraform Remote State Bootstrap ==="
echo "  Resource Group:  $RESOURCE_GROUP"
echo "  Storage Account: $STORAGE_ACCOUNT"
echo "  Container:       $CONTAINER_NAME"
echo "  Location:        $LOCATION"
echo ""

# 1. Create resource group
echo "Creating resource group..."
az group create \
  --name "$RESOURCE_GROUP" \
  --location "$LOCATION" \
  --tags project=smart-kb purpose=terraform-state managed_by=bootstrap \
  --output none

# 2. Create storage account
echo "Creating storage account..."
az storage account create \
  --name "$STORAGE_ACCOUNT" \
  --resource-group "$RESOURCE_GROUP" \
  --location "$LOCATION" \
  --sku Standard_LRS \
  --kind StorageV2 \
  --min-tls-version TLS1_2 \
  --allow-blob-public-access false \
  --https-only true \
  --tags project=smart-kb purpose=terraform-state managed_by=bootstrap \
  --output none

# 3. Enable blob versioning for state history
echo "Enabling blob versioning..."
az storage account blob-service-properties update \
  --account-name "$STORAGE_ACCOUNT" \
  --resource-group "$RESOURCE_GROUP" \
  --enable-versioning true \
  --enable-delete-retention true \
  --delete-retention-days 14 \
  --output none

# 4. Create blob container
echo "Creating blob container..."
az storage container create \
  --name "$CONTAINER_NAME" \
  --account-name "$STORAGE_ACCOUNT" \
  --auth-mode login \
  --output none 2>/dev/null || true

echo ""
echo "=== Bootstrap Complete ==="
echo ""
echo "Initialize Terraform with:"
echo "  terraform init -backend-config=backend.<env>.hcl"
echo ""
echo "Or supply values directly:"
echo "  terraform init \\"
echo "    -backend-config=\"resource_group_name=$RESOURCE_GROUP\" \\"
echo "    -backend-config=\"storage_account_name=$STORAGE_ACCOUNT\" \\"
echo "    -backend-config=\"container_name=$CONTAINER_NAME\" \\"
echo "    -backend-config=\"key=smartkb-<env>.tfstate\""
