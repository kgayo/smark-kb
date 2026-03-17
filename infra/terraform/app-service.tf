resource "azurerm_service_plan" "main" {
  name                = "plan-smartkb-${var.environment}"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  os_type             = "Linux"
  sku_name            = var.app_service_sku

  tags = local.common_tags
}

resource "azurerm_linux_web_app" "api" {
  name                = "app-smartkb-api-${var.environment}"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  service_plan_id     = azurerm_service_plan.main.id
  https_only          = true

  identity {
    type = "SystemAssigned"
  }

  site_config {
    application_stack {
      dotnet_version = "10.0"
    }
    always_on = var.environment != "dev"
  }

  app_settings = {
    "APPLICATIONINSIGHTS_CONNECTION_STRING"  = azurerm_application_insights.main.connection_string
    "KeyVault__VaultUri"                     = azurerm_key_vault.main.vault_uri
    "ServiceBus__FullyQualifiedNamespace"    = "${azurerm_servicebus_namespace.main.name}.servicebus.windows.net"
    "BlobStorage__ServiceUri"                = azurerm_storage_account.main.primary_blob_endpoint
    "SearchService__Endpoint"                = "https://${azurerm_search_service.main.name}.search.windows.net"
  }

  connection_string {
    name  = "SmartKbDb"
    type  = "SQLAzure"
    value = "Server=tcp:${azurerm_mssql_server.main.fully_qualified_domain_name},1433;Database=${azurerm_mssql_database.main.name};Authentication=Active Directory Default;"
  }

  tags = local.common_tags
}

resource "azurerm_linux_web_app" "ingestion" {
  name                = "app-smartkb-ingestion-${var.environment}"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  service_plan_id     = azurerm_service_plan.main.id
  https_only          = true

  identity {
    type = "SystemAssigned"
  }

  site_config {
    application_stack {
      dotnet_version = "10.0"
    }
    always_on = var.environment != "dev"
  }

  app_settings = {
    "APPLICATIONINSIGHTS_CONNECTION_STRING"  = azurerm_application_insights.main.connection_string
    "KeyVault__VaultUri"                     = azurerm_key_vault.main.vault_uri
    "ServiceBus__FullyQualifiedNamespace"    = "${azurerm_servicebus_namespace.main.name}.servicebus.windows.net"
    "BlobStorage__ServiceUri"                = azurerm_storage_account.main.primary_blob_endpoint
    "SearchService__Endpoint"                = "https://${azurerm_search_service.main.name}.search.windows.net"
  }

  connection_string {
    name  = "SmartKbDb"
    type  = "SQLAzure"
    value = "Server=tcp:${azurerm_mssql_server.main.fully_qualified_domain_name},1433;Database=${azurerm_mssql_database.main.name};Authentication=Active Directory Default;"
  }

  tags = local.common_tags
}
