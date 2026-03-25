/**
 * Shared CSS class helpers for trust level badges and sync status indicators.
 * Consolidates duplicate functions from PatternDetailView, PatternList,
 * ConnectorList, and SyncRunHistory.
 */

/** Maps a trust level string to its badge CSS class. */
export function trustLevelBadgeClass(level: string): string {
  switch (level) {
    case 'Draft': return 'trust-badge trust-draft';
    case 'Reviewed': return 'trust-badge trust-reviewed';
    case 'Approved': return 'trust-badge trust-approved';
    case 'Deprecated': return 'trust-badge trust-deprecated';
    default: return 'trust-badge';
  }
}

/** Maps a sync run status string to its CSS class. */
export function syncStatusClass(status: string): string {
  switch (status) {
    case 'Completed': return 'sync-status-completed';
    case 'Failed': return 'sync-status-failed';
    case 'Running': return 'sync-status-running';
    default: return 'sync-status-pending';
  }
}
