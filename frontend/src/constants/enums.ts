/**
 * Shared frontend enum constants — must match backend enum values
 * (SmartKb.Contracts/Enums/).
 *
 * Eliminates hardcoded string literals scattered across components.
 */

// ── Connector types ──

export const ConnectorTypes = {
  AzureDevOps: 'AzureDevOps',
  SharePoint: 'SharePoint',
  HubSpot: 'HubSpot',
  ClickUp: 'ClickUp',
} as const;

// ── Connector statuses ──

export const ConnectorStatuses = {
  Enabled: 'Enabled',
  Disabled: 'Disabled',
} as const;

// ── Sync run statuses ──

export const SyncRunStatuses = {
  Pending: 'Pending',
  Running: 'Running',
  Completed: 'Completed',
  Failed: 'Failed',
} as const;

// ── Trust levels ──

export const TrustLevels = {
  Draft: 'Draft',
  Reviewed: 'Reviewed',
  Approved: 'Approved',
  Deprecated: 'Deprecated',
} as const;
