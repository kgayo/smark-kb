resource "azurerm_search_service" "main" {
  name                = "srch-smartkb-${var.environment}"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  sku                 = var.search_sku

  identity {
    type = "SystemAssigned"
  }

  tags = local.common_tags
}
