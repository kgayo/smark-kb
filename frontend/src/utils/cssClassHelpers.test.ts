import { describe, it, expect } from 'vitest';
import { trustLevelBadgeClass, syncStatusClass } from './cssClassHelpers';
import { TrustLevels, SyncRunStatuses } from '../constants/enums';

describe('trustLevelBadgeClass', () => {
  it('returns correct class for Draft', () => {
    expect(trustLevelBadgeClass(TrustLevels.Draft)).toBe('trust-badge trust-draft');
  });

  it('returns correct class for Reviewed', () => {
    expect(trustLevelBadgeClass(TrustLevels.Reviewed)).toBe('trust-badge trust-reviewed');
  });

  it('returns correct class for Approved', () => {
    expect(trustLevelBadgeClass(TrustLevels.Approved)).toBe('trust-badge trust-approved');
  });

  it('returns correct class for Deprecated', () => {
    expect(trustLevelBadgeClass(TrustLevels.Deprecated)).toBe('trust-badge trust-deprecated');
  });

  it('returns base class for unknown level', () => {
    expect(trustLevelBadgeClass('Unknown')).toBe('trust-badge');
  });
});

describe('syncStatusClass', () => {
  it('returns correct class for Completed', () => {
    expect(syncStatusClass(SyncRunStatuses.Completed)).toBe('sync-status-completed');
  });

  it('returns correct class for Failed', () => {
    expect(syncStatusClass(SyncRunStatuses.Failed)).toBe('sync-status-failed');
  });

  it('returns correct class for Running', () => {
    expect(syncStatusClass(SyncRunStatuses.Running)).toBe('sync-status-running');
  });

  it('returns pending class for unknown status', () => {
    expect(syncStatusClass(SyncRunStatuses.Pending)).toBe('sync-status-pending');
  });

  it('returns pending class for empty string', () => {
    expect(syncStatusClass('')).toBe('sync-status-pending');
  });
});
