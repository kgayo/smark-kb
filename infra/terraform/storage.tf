resource "azurerm_storage_account" "main" {
  name                     = "stsmartkb${var.environment}"
  location                 = azurerm_resource_group.main.location
  resource_group_name      = azurerm_resource_group.main.name
  account_tier             = "Standard"
  account_replication_type = "LRS"
  min_tls_version          = "TLS1_2"

  identity {
    type = var.enable_cmk ? "SystemAssigned, UserAssigned" : "SystemAssigned"
    identity_ids = var.enable_cmk ? [azurerm_user_assigned_identity.cmk[0].id] : []
  }

  dynamic "customer_managed_key" {
    for_each = var.enable_cmk ? [1] : []
    content {
      key_vault_key_id          = var.cmk_key_vault_key_id
      user_assigned_identity_id = azurerm_user_assigned_identity.cmk[0].id
    }
  }

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

resource "azurerm_role_assignment" "api_storage_blob_contributor" {
  scope                = azurerm_storage_account.main.id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = azurerm_linux_web_app.api.identity[0].principal_id
}

resource "azurerm_role_assignment" "ingestion_storage_blob_contributor" {
  scope                = azurerm_storage_account.main.id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = azurerm_linux_web_app.ingestion.identity[0].principal_id
}
