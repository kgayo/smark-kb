variable "infra_version" {
  description = "Infrastructure template version (semver). Must match ARM contentVersion and infra/CHANGELOG.md."
  type        = string
  default     = "1.6.0"
  validation {
    condition     = can(regex("^\\d+\\.\\d+\\.\\d+$", var.infra_version))
    error_message = "infra_version must be a semantic version (e.g. 1.6.0)."
  }
}

variable "subscription_id" {
  description = "Azure subscription ID."
  type        = string
}

variable "environment" {
  description = "Deployment environment (dev, staging, prod)."
  type        = string
  validation {
    condition     = contains(["dev", "staging", "prod"], var.environment)
    error_message = "Environment must be dev, staging, or prod."
  }
}

variable "location" {
  description = "Azure region for all resources."
  type        = string
  default     = "eastus"
}

variable "sql_admin_login" {
  description = "SQL Server administrator login."
  type        = string
  default     = "smartkbadmin"
}

variable "sql_admin_password" {
  description = "SQL Server administrator password."
  type        = string
  sensitive   = true
}

variable "entra_tenant_id" {
  description = "Microsoft Entra ID tenant ID for authentication."
  type        = string
}

variable "app_service_sku" {
  description = "App Service Plan SKU name."
  type        = string
  default     = "B1"
}

variable "sql_sku" {
  description = "Azure SQL Database SKU name."
  type        = string
  default     = "Basic"
}

variable "search_sku" {
  description = "Azure AI Search SKU."
  type        = string
  default     = "basic"
}

variable "servicebus_sku" {
  description = "Service Bus namespace SKU."
  type        = string
  default     = "Basic"
}

variable "static_web_app_sku" {
  description = "Azure Static Web App SKU tier (Free or Standard)."
  type        = string
  default     = "Free"
  validation {
    condition     = contains(["Free", "Standard"], var.static_web_app_sku)
    error_message = "Static Web App SKU must be Free or Standard."
  }
}

# --- SLO Alert Thresholds (P0-022) ---

variable "chat_latency_p95_threshold_ms" {
  description = "P95 chat latency threshold in milliseconds for alert."
  type        = number
  default     = 8000
}

variable "availability_threshold_percent" {
  description = "Availability percentage threshold for alert."
  type        = number
  default     = 99.5
}

variable "dead_letter_threshold" {
  description = "Dead-letter message count threshold for alert."
  type        = number
  default     = 10
}

variable "http_5xx_threshold" {
  description = "HTTP 5xx error count threshold per 5-minute window."
  type        = number
  default     = 5
}

variable "queue_backlog_threshold" {
  description = "Service Bus active message backlog threshold."
  type        = number
  default     = 100
}
