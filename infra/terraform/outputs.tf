output "resource_group_name" {
  value = azurerm_resource_group.main.name
}

output "app_service_name" {
  value = azurerm_linux_web_app.api.name
}

output "app_service_default_hostname" {
  value = azurerm_linux_web_app.api.default_hostname
}

output "sql_server_fqdn" {
  value = azurerm_mssql_server.main.fully_qualified_domain_name
}

output "sql_database_name" {
  value = azurerm_mssql_database.main.name
}

output "search_service_name" {
  value = azurerm_search_service.main.name
}

output "search_service_endpoint" {
  value = "https://${azurerm_search_service.main.name}.search.windows.net"
}

output "storage_account_name" {
  value = azurerm_storage_account.main.name
}

output "key_vault_uri" {
  value = azurerm_key_vault.main.vault_uri
}

output "servicebus_namespace" {
  value = azurerm_servicebus_namespace.main.name
}

output "servicebus_fully_qualified_namespace" {
  value = "${azurerm_servicebus_namespace.main.name}.servicebus.windows.net"
}

output "ingestion_app_service_name" {
  value = azurerm_linux_web_app.ingestion.name
}

output "ingestion_app_service_default_hostname" {
  value = azurerm_linux_web_app.ingestion.default_hostname
}

output "application_insights_connection_string" {
  value     = azurerm_application_insights.main.connection_string
  sensitive = true
}

output "static_web_app_name" {
  value = azurerm_static_web_app.frontend.name
}

output "static_web_app_default_hostname" {
  value = azurerm_static_web_app.frontend.default_host_name
}

output "infra_version" {
  description = "Infrastructure template version. Must match ARM metadata.infraVersion and infra/CHANGELOG.md."
  value       = var.infra_version
}
