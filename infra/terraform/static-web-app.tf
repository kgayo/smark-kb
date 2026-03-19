# --- Azure Static Web App for frontend hosting (P3-036) ---

resource "azurerm_static_web_app" "frontend" {
  name                = "stapp-smartkb-${var.environment}"
  resource_group_name = azurerm_resource_group.main.name
  location            = var.location
  sku_tier            = var.static_web_app_sku
  sku_size            = var.static_web_app_sku

  tags = local.common_tags
}
