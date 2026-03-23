# Customer-managed key (CMK) resources (P3-030).
# Conditionally created when var.enable_cmk = true.

resource "azurerm_user_assigned_identity" "cmk" {
  count               = var.enable_cmk ? 1 : 0
  name                = "id-smartkb-cmk-${var.environment}"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name

  tags = local.common_tags
}

# Grant the CMK identity Key Vault Crypto Officer so it can use the
# encryption key for wrap/unwrap operations.
resource "azurerm_role_assignment" "cmk_keyvault_crypto" {
  count                = var.enable_cmk ? 1 : 0
  scope                = azurerm_key_vault.main.id
  role_definition_name = "Key Vault Crypto Officer"
  principal_id         = azurerm_user_assigned_identity.cmk[0].principal_id
}
