# Remote state backend using Azure Storage with blob lease locking.
#
# This uses partial configuration — supply actual values at init time:
#   terraform init \
#     -backend-config="resource_group_name=rg-smartkb-tfstate" \
#     -backend-config="storage_account_name=stsmartkbtfstate" \
#     -backend-config="container_name=tfstate" \
#     -backend-config="key=smartkb-dev.tfstate"
#
# Or via a backend config file:
#   terraform init -backend-config=backend.dev.hcl
#
# The state storage account is bootstrapped separately (see infra/scripts/bootstrap-tfstate.sh).

terraform {
  backend "azurerm" {
    use_azuread_auth = true
  }
}
