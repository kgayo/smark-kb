# Azure Monitor Action Group for SLO alerts (P0-022).
resource "azurerm_monitor_action_group" "slo" {
  name                = "ag-smartkb-slo-${var.environment}"
  resource_group_name = azurerm_resource_group.main.name
  short_name          = "smartkb-slo"

  tags = local.common_tags
}

# --- Critical Alerts ---

# P95 Chat Latency > 8s (answer-ready SLO).
resource "azurerm_monitor_metric_alert" "chat_latency_p95" {
  name                = "alert-smartkb-chat-latency-p95-${var.environment}"
  resource_group_name = azurerm_resource_group.main.name
  scopes              = [azurerm_application_insights.main.id]
  description         = "P95 chat orchestration latency exceeds ${var.chat_latency_p95_threshold_ms}ms target."
  severity            = 1
  frequency           = "PT5M"
  window_size         = "PT5M"
  enabled             = var.environment != "dev"

  criteria {
    metric_namespace = "azure.applicationinsights"
    metric_name      = "smartkb.chat.latency_ms"
    aggregation      = "Average"
    operator         = "GreaterThan"
    threshold        = var.chat_latency_p95_threshold_ms
  }

  action {
    action_group_id = azurerm_monitor_action_group.slo.id
  }

  tags = local.common_tags
}

# API Availability < 99.5%.
resource "azurerm_monitor_metric_alert" "api_availability" {
  name                = "alert-smartkb-api-availability-${var.environment}"
  resource_group_name = azurerm_resource_group.main.name
  scopes              = [azurerm_application_insights.main.id]
  description         = "API availability dropped below ${var.availability_threshold_percent}%."
  severity            = 1
  frequency           = "PT5M"
  window_size         = "PT15M"
  enabled             = var.environment != "dev"

  criteria {
    metric_namespace = "microsoft.insights/components"
    metric_name      = "availabilityResults/availabilityPercentage"
    aggregation      = "Average"
    operator         = "LessThan"
    threshold        = var.availability_threshold_percent
  }

  action {
    action_group_id = azurerm_monitor_action_group.slo.id
  }

  tags = local.common_tags
}

# Dead-letter queue depth growing.
resource "azurerm_monitor_metric_alert" "dead_letter_depth" {
  name                = "alert-smartkb-dead-letter-depth-${var.environment}"
  resource_group_name = azurerm_resource_group.main.name
  scopes              = [azurerm_servicebus_namespace.main.id]
  description         = "Dead-letter message count exceeds ${var.dead_letter_threshold} in ingestion queue."
  severity            = 2
  frequency           = "PT5M"
  window_size         = "PT15M"
  enabled             = var.environment != "dev"

  criteria {
    metric_namespace = "Microsoft.ServiceBus/namespaces"
    metric_name      = "DeadletteredMessages"
    aggregation      = "Maximum"
    operator         = "GreaterThan"
    threshold        = var.dead_letter_threshold
  }

  action {
    action_group_id = azurerm_monitor_action_group.slo.id
  }

  tags = local.common_tags
}

# --- Warning Alerts ---

# API HTTP 5xx error rate.
resource "azurerm_monitor_metric_alert" "api_server_errors" {
  name                = "alert-smartkb-api-5xx-${var.environment}"
  resource_group_name = azurerm_resource_group.main.name
  scopes              = [azurerm_linux_web_app.api.id]
  description         = "API returning elevated 5xx server errors."
  severity            = 2
  frequency           = "PT5M"
  window_size         = "PT5M"
  enabled             = var.environment != "dev"

  criteria {
    metric_namespace = "Microsoft.Web/sites"
    metric_name      = "Http5xx"
    aggregation      = "Total"
    operator         = "GreaterThan"
    threshold        = var.http_5xx_threshold
  }

  action {
    action_group_id = azurerm_monitor_action_group.slo.id
  }

  tags = local.common_tags
}

# Ingestion worker HTTP 5xx error rate.
resource "azurerm_monitor_metric_alert" "ingestion_server_errors" {
  name                = "alert-smartkb-ingestion-5xx-${var.environment}"
  resource_group_name = azurerm_resource_group.main.name
  scopes              = [azurerm_linux_web_app.ingestion.id]
  description         = "Ingestion worker returning elevated 5xx server errors."
  severity            = 2
  frequency           = "PT5M"
  window_size         = "PT5M"
  enabled             = var.environment != "dev"

  criteria {
    metric_namespace = "Microsoft.Web/sites"
    metric_name      = "Http5xx"
    aggregation      = "Total"
    operator         = "GreaterThan"
    threshold        = var.http_5xx_threshold
  }

  action {
    action_group_id = azurerm_monitor_action_group.slo.id
  }

  tags = local.common_tags
}

# Service Bus active message queue depth (ingestion backlog).
resource "azurerm_monitor_metric_alert" "ingestion_backlog" {
  name                = "alert-smartkb-ingestion-backlog-${var.environment}"
  resource_group_name = azurerm_resource_group.main.name
  scopes              = [azurerm_servicebus_namespace.main.id]
  description         = "Ingestion queue backlog exceeds ${var.queue_backlog_threshold} messages."
  severity            = 3
  frequency           = "PT5M"
  window_size         = "PT15M"
  enabled             = var.environment != "dev"

  criteria {
    metric_namespace = "Microsoft.ServiceBus/namespaces"
    metric_name      = "ActiveMessages"
    aggregation      = "Maximum"
    operator         = "GreaterThan"
    threshold        = var.queue_backlog_threshold
  }

  action {
    action_group_id = azurerm_monitor_action_group.slo.id
  }

  tags = local.common_tags
}
