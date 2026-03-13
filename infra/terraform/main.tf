terraform {
  required_version = ">= 1.5.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.0"
    }
  }
}

provider "azurerm" {
  features {
    key_vault {
      purge_soft_delete_on_destroy = false
    }
  }

  subscription_id = var.subscription_id
}

resource "azurerm_resource_group" "main" {
  name     = "rg-smartkb-${var.environment}"
  location = var.location

  tags = local.common_tags
}

locals {
  common_tags = {
    project     = "smart-kb"
    environment = var.environment
    managed_by  = "terraform"
  }
}
