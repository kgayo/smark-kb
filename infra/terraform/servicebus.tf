resource "azurerm_servicebus_namespace" "main" {
  name                = "sb-smartkb-${var.environment}"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  sku                 = var.servicebus_sku

  tags = local.common_tags
}

resource "azurerm_servicebus_queue" "ingestion" {
  name         = "ingestion-jobs"
  namespace_id = azurerm_servicebus_namespace.main.id

  max_delivery_count      = 10
  dead_lettering_on_message_expiration = true
}
