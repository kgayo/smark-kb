/**
 * Shared CSS class helpers for trust level badges and sync status indicators.
 * Consolidates duplicate functions from PatternDetailView, PatternList,
 * ConnectorList, and SyncRunHistory.
 */

import { TrustLevels, SyncRunStatuses } from '../constants/enums';

/** Maps a trust level string to its badge CSS class. */
export function trustLevelBadgeClass(level: string): string {
  switch (level) {
    case TrustLevels.Draft: return 'trust-badge trust-draft';
    case TrustLevels.Reviewed: return 'trust-badge trust-reviewed';
    case TrustLevels.Approved: return 'trust-badge trust-approved';
    case TrustLevels.Deprecated: return 'trust-badge trust-deprecated';
    default: return 'trust-badge';
  }
}

/** Maps a sync run status string to its CSS class. */
export function syncStatusClass(status: string): string {
  switch (status) {
    case SyncRunStatuses.Completed: return 'sync-status-completed';
    case SyncRunStatuses.Failed: return 'sync-status-failed';
    case SyncRunStatuses.Running: return 'sync-status-running';
    default: return 'sync-status-pending';
  }
}
