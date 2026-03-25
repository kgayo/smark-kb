import React, { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import type {
  AuditEventListResponse,
  AuditEventQueryParams,
  AuditEventResponse,
  AuditExportParams,
} from '../api/types';
import * as api from '../api/client';
import { useRoles, hasAdminRole } from '../auth/useRoles';
import { downloadFile } from '../utils/downloadFile';

type Tab = 'events' | 'export';

export function AuditCompliancePage() {
  const { roles, loading: rolesLoading } = useRoles();
  const [tab, setTab] = useState<Tab>('events');

  // ── Events tab state ──
  const [events, setEvents] = useState<AuditEventListResponse | null>(null);
  const [eventsLoading, setEventsLoading] = useState(false);
  const [eventsError, setEventsError] = useState<string | null>(null);
  const [filters, setFilters] = useState<AuditEventQueryParams>({ page: 1, pageSize: 50 });
  const [expandedEventId, setExpandedEventId] = useState<string | null>(null);

  // ── Export tab state ──
  const [exportFilters, setExportFilters] = useState<AuditExportParams>({});
  const [exporting, setExporting] = useState(false);
  const [exportError, setExportError] = useState<string | null>(null);
  const [exportSuccess, setExportSuccess] = useState<string | null>(null);

  const loadEvents = useCallback(async (params: AuditEventQueryParams) => {
    setEventsLoading(true);
    setEventsError(null);
    try {
      const result = await api.queryAuditEvents(params);
      setEvents(result);
    } catch (e) {
      setEventsError(e instanceof Error ? e.message : 'Failed to load audit events');
    } finally {
      setEventsLoading(false);
    }
  }, []);

  useEffect(() => {
    if (!hasAdminRole(roles) || tab !== 'events') return;
    loadEvents(filters);
  }, [roles, tab, filters, loadEvents]);

  const handleFilterChange = (key: keyof AuditEventQueryParams, value: string) => {
    setFilters((prev) => ({ ...prev, [key]: value || undefined, page: 1 }));
  };

  const handlePageChange = (newPage: number) => {
    setFilters((prev) => ({ ...prev, page: newPage }));
  };

  const handleExport = async () => {
    setExporting(true);
    setExportError(null);
    setExportSuccess(null);
    try {
      const blob = await api.exportAuditEvents(exportFilters);
      const ts = new Date().toISOString().replace(/[:.]/g, '-');
      downloadFile(blob, `audit-events-${ts}.ndjson`);
      setExportSuccess('Export downloaded successfully.');
    } catch (e) {
      setExportError(e instanceof Error ? e.message : 'Export failed');
    } finally {
      setExporting(false);
    }
  };

  const handleExportFilterChange = (key: keyof AuditExportParams, value: string) => {
    setExportFilters((prev) => ({ ...prev, [key]: value || undefined }));
  };

  if (rolesLoading) {
    return (
      <div className="admin-loading" data-testid="audit-loading">
        <p>Loading...</p>
      </div>
    );
  }

  if (!hasAdminRole(roles)) {
    return (
      <div className="admin-denied" data-testid="audit-denied">
        <h1>Access Denied</h1>
        <p>You need the Admin role to access audit &amp; compliance.</p>
        <Link to="/" className="btn btn-primary">Back to Chat</Link>
      </div>
    );
  }

  return (
    <div className="admin-layout" data-testid="audit-compliance-page">
      <header className="admin-header">
        <div className="admin-header-left">
          <h1>Audit &amp; Compliance</h1>
        </div>
        <div className="admin-header-right">
          <Link to="/admin" className="btn btn-sm">Connectors</Link>
          <Link to="/diagnostics" className="btn btn-sm">Diagnostics</Link>
          <Link to="/privacy" className="btn btn-sm">Privacy</Link>
          <Link to="/" className="btn btn-sm">Back to Chat</Link>
        </div>
      </header>

      <div className="admin-tabs" data-testid="audit-tabs">
        <button
          className={`admin-tab${tab === 'events' ? ' active' : ''}`}
          onClick={() => setTab('events')}
          data-testid="tab-events"
          aria-label="Events tab"
        >
          Events
        </button>
        <button
          className={`admin-tab${tab === 'export' ? ' active' : ''}`}
          onClick={() => setTab('export')}
          data-testid="tab-export"
          aria-label="Export tab"
        >
          Export
        </button>
      </div>

      <main className="admin-main">
        {tab === 'events' && (
          <EventsPanel
            events={events}
            loading={eventsLoading}
            error={eventsError}
            filters={filters}
            expandedEventId={expandedEventId}
            onFilterChange={handleFilterChange}
            onPageChange={handlePageChange}
            onToggleExpand={(id) =>
              setExpandedEventId((prev) => (prev === id ? null : id))
            }
          />
        )}
        {tab === 'export' && (
          <ExportPanel
            filters={exportFilters}
            exporting={exporting}
            error={exportError}
            success={exportSuccess}
            onFilterChange={handleExportFilterChange}
            onExport={handleExport}
          />
        )}
      </main>
    </div>
  );
}

// ── Events Panel ──

interface EventsPanelProps {
  events: AuditEventListResponse | null;
  loading: boolean;
  error: string | null;
  filters: AuditEventQueryParams;
  expandedEventId: string | null;
  onFilterChange: (key: keyof AuditEventQueryParams, value: string) => void;
  onPageChange: (page: number) => void;
  onToggleExpand: (id: string) => void;
}

function EventsPanel({
  events,
  loading,
  error,
  filters,
  expandedEventId,
  onFilterChange,
  onPageChange,
  onToggleExpand,
}: EventsPanelProps) {
  return (
    <div data-testid="events-panel">
      <div className="admin-toolbar" data-testid="audit-filters">
        <input
          type="text"
          placeholder="Event type"
          aria-label="Filter by event type"
          value={filters.eventType ?? ''}
          onChange={(e) => onFilterChange('eventType', e.target.value)}
          data-testid="filter-event-type"
        />
        <input
          type="text"
          placeholder="Actor ID"
          aria-label="Filter by actor ID"
          value={filters.actorId ?? ''}
          onChange={(e) => onFilterChange('actorId', e.target.value)}
          data-testid="filter-actor-id"
        />
        <input
          type="text"
          placeholder="Correlation ID"
          aria-label="Filter by correlation ID"
          value={filters.correlationId ?? ''}
          onChange={(e) => onFilterChange('correlationId', e.target.value)}
          data-testid="filter-correlation-id"
        />
        <input
          type="datetime-local"
          aria-label="From date"
          value={filters.from ?? ''}
          onChange={(e) => onFilterChange('from', e.target.value)}
          data-testid="filter-from"
        />
        <input
          type="datetime-local"
          aria-label="To date"
          value={filters.to ?? ''}
          onChange={(e) => onFilterChange('to', e.target.value)}
          data-testid="filter-to"
        />
      </div>

      {error && <div className="error-banner" role="alert" data-testid="events-error">{error}</div>}
      {loading && <p>Loading events...</p>}

      {events && !loading && (
        <>
          <p className="audit-summary" data-testid="events-summary">
            Showing {events.events.length} of {events.totalCount} events (page {events.page})
          </p>
          <table className="admin-table" data-testid="events-table" aria-label="Audit events">
            <thead>
              <tr>
                <th>Timestamp</th>
                <th>Event Type</th>
                <th>Actor</th>
                <th>Correlation ID</th>
              </tr>
            </thead>
            <tbody>
              {events.events.map((evt) => (
                <React.Fragment key={evt.eventId}>
                  <tr
                    onClick={() => onToggleExpand(evt.eventId)}
                    className={expandedEventId === evt.eventId ? 'expanded' : ''}
                    data-testid={`event-row-${evt.eventId}`}
                    style={{ cursor: 'pointer' }}
                    aria-label={`Toggle details for ${evt.eventType} event`}
                  >
                    <td>{formatTimestamp(evt.timestamp)}</td>
                    <td>
                      <span className="status-badge">{evt.eventType}</span>
                    </td>
                    <td>{evt.actorId}</td>
                    <td className="audit-correlation">{evt.correlationId}</td>
                  </tr>
                  {expandedEventId === evt.eventId && (
                    <tr className="audit-detail-row" data-testid={`event-detail-${evt.eventId}`}>
                      <td colSpan={4}>
                        <EventDetail event={evt} />
                      </td>
                    </tr>
                  )}
                </React.Fragment>
              ))}
              {events.events.length === 0 && (
                <tr>
                  <td colSpan={4} style={{ textAlign: 'center' }}>
                    No audit events found matching the filters.
                  </td>
                </tr>
              )}
            </tbody>
          </table>

          <div className="audit-pagination" data-testid="events-pagination">
            <button
              className="btn btn-sm"
              disabled={events.page <= 1}
              onClick={() => onPageChange(events.page - 1)}
              data-testid="page-prev"
              aria-label="Previous page"
            >
              Previous
            </button>
            <span>
              Page {events.page} of {Math.max(1, Math.ceil(events.totalCount / events.pageSize))}
            </span>
            <button
              className="btn btn-sm"
              disabled={!events.hasMore}
              onClick={() => onPageChange(events.page + 1)}
              data-testid="page-next"
              aria-label="Next page"
            >
              Next
            </button>
          </div>
        </>
      )}
    </div>
  );
}

// ── Event Detail ──

function EventDetail({ event }: { event: AuditEventResponse }) {
  let parsedDetail: Record<string, unknown> | null = null;
  try {
    parsedDetail = JSON.parse(event.detail);
  } catch {
    // detail is not JSON
  }

  return (
    <div className="audit-event-detail" data-testid="event-detail-content">
      <div className="admin-info-grid">
        <span className="info-label">Event ID</span>
        <span className="info-value">{event.eventId}</span>
        <span className="info-label">Tenant ID</span>
        <span className="info-value">{event.tenantId}</span>
        <span className="info-label">Actor ID</span>
        <span className="info-value">{event.actorId}</span>
        <span className="info-label">Correlation ID</span>
        <span className="info-value">{event.correlationId}</span>
        <span className="info-label">Timestamp</span>
        <span className="info-value">{event.timestamp}</span>
      </div>
      <h4>Detail</h4>
      {parsedDetail ? (
        <pre className="audit-detail-json" data-testid="event-detail-json">
          {JSON.stringify(parsedDetail, null, 2)}
        </pre>
      ) : (
        <pre className="audit-detail-json" data-testid="event-detail-raw">
          {event.detail}
        </pre>
      )}
    </div>
  );
}

// ── Export Panel ──

interface ExportPanelProps {
  filters: AuditExportParams;
  exporting: boolean;
  error: string | null;
  success: string | null;
  onFilterChange: (key: keyof AuditExportParams, value: string) => void;
  onExport: () => void;
}

function ExportPanel({ filters, exporting, error, success, onFilterChange, onExport }: ExportPanelProps) {
  return (
    <div data-testid="export-panel">
      <p>Export audit events as NDJSON (newline-delimited JSON) for compliance investigations.</p>

      <div className="admin-form-inline" data-testid="export-filters">
        <div className="admin-form-row">
          <label>
            Event Type
            <input
              type="text"
              placeholder="e.g. connector.created"
              value={filters.eventType ?? ''}
              onChange={(e) => onFilterChange('eventType', e.target.value)}
              data-testid="export-filter-event-type"
            />
          </label>
          <label>
            Actor ID
            <input
              type="text"
              placeholder="Filter by actor"
              value={filters.actorId ?? ''}
              onChange={(e) => onFilterChange('actorId', e.target.value)}
              data-testid="export-filter-actor-id"
            />
          </label>
        </div>
        <div className="admin-form-row">
          <label>
            From
            <input
              type="datetime-local"
              value={filters.from ?? ''}
              onChange={(e) => onFilterChange('from', e.target.value)}
              data-testid="export-filter-from"
            />
          </label>
          <label>
            To
            <input
              type="datetime-local"
              value={filters.to ?? ''}
              onChange={(e) => onFilterChange('to', e.target.value)}
              data-testid="export-filter-to"
            />
          </label>
        </div>
        <div className="admin-form-actions">
          <button
            className="btn btn-primary"
            onClick={onExport}
            disabled={exporting}
            data-testid="export-download-btn"
            aria-label={exporting ? 'Exporting audit events' : 'Download NDJSON export'}
          >
            {exporting ? 'Exporting...' : 'Download NDJSON Export'}
          </button>
        </div>
      </div>

      {error && <div className="error-banner" data-testid="export-error">{error}</div>}
      {success && <div className="success-banner" data-testid="export-success">{success}</div>}
    </div>
  );
}

// ── Helpers ──

function formatTimestamp(iso: string): string {
  try {
    const d = new Date(iso);
    return d.toLocaleString();
  } catch (e) {
    console.warn('[AuditCompliancePage] Failed to format timestamp', e);
    return iso;
  }
}
