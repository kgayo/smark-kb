resource "azurerm_storage_account" "main" {
  name                     = "stsmartkb${var.environment}"
  location                 = azurerm_resource_group.main.location
  resource_group_name      = azurerm_resource_group.main.name
  account_tier             = "Standard"
  account_replication_type = "LRS"
  min_tls_version          = "TLS1_2"

  blob_properties {
    delete_retention_policy {
      days = 7
    }
  }

  tags = local.common_tags
}

resource "azurerm_storage_container" "raw_content" {
  name                  = "raw-content"
  storage_account_id    = azurerm_storage_account.main.id
  container_access_type = "private"
}
