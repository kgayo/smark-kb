/**
 * Shared date formatting utilities.
 * Consolidates duplicate formatDate/formatTime/formatTimestamp helpers
 * from 8 component and page files.
 */
import { logger } from './logger';

/** Full date+time: "Mar 25, 2026, 02:30 PM" */
export function formatDateTime(iso: string): string {
  return new Date(iso).toLocaleDateString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}

/** Full date+time with null guard, returns "—" for falsy input. */
export function formatDateTimeOrDash(iso: string | null): string {
  if (!iso) return '—';
  return formatDateTime(iso);
}

/** Locale-default date+time string (e.g. "3/25/2026, 2:30:00 PM"). */
export function formatDateTimeLocale(iso: string): string {
  try {
    return new Date(iso).toLocaleString();
  } catch (e) {
    logger.warn('[dateFormat] Failed to format date', e);
    return iso;
  }
}

/** Date-only: "Mar 25, 2026" */
export function formatDateOnly(iso: string): string {
  return new Date(iso).toLocaleDateString(undefined, {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
  });
}

/** Relative time: "just now", "5m ago", "3h ago", or fallback to locale date. */
export function formatRelativeTime(iso: string): string {
  try {
    const d = new Date(iso);
    const now = Date.now();
    const diff = now - d.getTime();
    if (diff < 60000) return 'just now';
    if (diff < 3600000) return `${Math.floor(diff / 60000)}m ago`;
    if (diff < 86400000) return `${Math.floor(diff / 3600000)}h ago`;
    return d.toLocaleDateString();
  } catch (e) {
    logger.warn('[dateFormat] Failed to format relative time', e);
    return iso;
  }
}
