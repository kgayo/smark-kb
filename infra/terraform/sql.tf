resource "azurerm_mssql_server" "main" {
  name                         = "sql-smartkb-${var.environment}"
  location                     = azurerm_resource_group.main.location
  resource_group_name          = azurerm_resource_group.main.name
  version                      = "12.0"
  administrator_login          = var.sql_admin_login
  administrator_login_password = var.sql_admin_password

  azuread_administrator {
    login_username = "smartkb-admin"
    tenant_id      = var.entra_tenant_id
    object_id      = azurerm_linux_web_app.api.identity[0].principal_id
  }

  tags = local.common_tags
}

resource "azurerm_mssql_database" "main" {
  name      = "sqldb-smartkb-${var.environment}"
  server_id = azurerm_mssql_server.main.id
  sku_name  = var.sql_sku

  tags = local.common_tags
}

resource "azurerm_mssql_firewall_rule" "allow_azure" {
  name             = "AllowAzureServices"
  server_id        = azurerm_mssql_server.main.id
  start_ip_address = "0.0.0.0"
  end_ip_address   = "0.0.0.0"
}
