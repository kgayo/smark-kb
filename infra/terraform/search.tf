resource "azurerm_search_service" "main" {
  name                = "srch-smartkb-${var.environment}"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  sku                 = var.search_sku

  identity {
    type         = var.enable_cmk ? "SystemAssigned, UserAssigned" : "SystemAssigned"
    identity_ids = var.enable_cmk ? [azurerm_user_assigned_identity.cmk[0].id] : []
  }

  customer_managed_key_enforcement_enabled = var.enable_cmk

  tags = local.common_tags
}

resource "azurerm_role_assignment" "api_search_index_data_contributor" {
  scope                = azurerm_search_service.main.id
  role_definition_name = "Search Index Data Contributor"
  principal_id         = azurerm_linux_web_app.api.identity[0].principal_id
}

resource "azurerm_role_assignment" "ingestion_search_index_data_contributor" {
  scope                = azurerm_search_service.main.id
  role_definition_name = "Search Index Data Contributor"
  principal_id         = azurerm_linux_web_app.ingestion.identity[0].principal_id
}

resource "azurerm_role_assignment" "api_search_service_contributor" {
  scope                = azurerm_search_service.main.id
  role_definition_name = "Search Service Contributor"
  principal_id         = azurerm_linux_web_app.api.identity[0].principal_id
}

resource "azurerm_role_assignment" "ingestion_search_service_contributor" {
  scope                = azurerm_search_service.main.id
  role_definition_name = "Search Service Contributor"
  principal_id         = azurerm_linux_web_app.ingestion.identity[0].principal_id
}
