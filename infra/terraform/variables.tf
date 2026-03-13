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
