import { describe, it, expect } from 'vitest';
import { trustLevelBadgeClass, syncStatusClass } from './cssClassHelpers';

describe('trustLevelBadgeClass', () => {
  it('returns correct class for Draft', () => {
    expect(trustLevelBadgeClass('Draft')).toBe('trust-badge trust-draft');
  });

  it('returns correct class for Reviewed', () => {
    expect(trustLevelBadgeClass('Reviewed')).toBe('trust-badge trust-reviewed');
  });

  it('returns correct class for Approved', () => {
    expect(trustLevelBadgeClass('Approved')).toBe('trust-badge trust-approved');
  });

  it('returns correct class for Deprecated', () => {
    expect(trustLevelBadgeClass('Deprecated')).toBe('trust-badge trust-deprecated');
  });

  it('returns base class for unknown level', () => {
    expect(trustLevelBadgeClass('Unknown')).toBe('trust-badge');
  });
});

describe('syncStatusClass', () => {
  it('returns correct class for Completed', () => {
    expect(syncStatusClass('Completed')).toBe('sync-status-completed');
  });

  it('returns correct class for Failed', () => {
    expect(syncStatusClass('Failed')).toBe('sync-status-failed');
  });

  it('returns correct class for Running', () => {
    expect(syncStatusClass('Running')).toBe('sync-status-running');
  });

  it('returns pending class for unknown status', () => {
    expect(syncStatusClass('Pending')).toBe('sync-status-pending');
  });

  it('returns pending class for empty string', () => {
    expect(syncStatusClass('')).toBe('sync-status-pending');
  });
});
