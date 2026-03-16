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

  max_delivery_count                   = 10
  dead_lettering_on_message_expiration = true
}

resource "azurerm_role_assignment" "api_servicebus_sender" {
  scope                = azurerm_servicebus_namespace.main.id
  role_definition_name = "Azure Service Bus Data Sender"
  principal_id         = azurerm_linux_web_app.api.identity[0].principal_id
}

resource "azurerm_role_assignment" "ingestion_servicebus_receiver" {
  scope                = azurerm_servicebus_namespace.main.id
  role_definition_name = "Azure Service Bus Data Receiver"
  principal_id         = azurerm_linux_web_app.ingestion.identity[0].principal_id
}
