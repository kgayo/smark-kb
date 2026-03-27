import { useCallback, useEffect, useState } from 'react';
import { logger } from '../utils/logger';
import type {
  ConnectorResponse,
  ConnectorValidationResult,
  FieldMappingConfig,
  PreviewRetrievalChunk,
  PreviewRecord,
  SyncRunSummary,
  TestConnectionResponse,
  UpdateConnectorRequest,
} from '../api/types';
import * as api from '../api/client';
import { ConnectorStatuses } from '../constants/enums';
import { FieldMappingEditor } from './FieldMappingEditor';
import { SourceConfigEditor } from './SourceConfigEditor';
import { SyncRunHistory } from './SyncRunHistory';

interface ConnectorDetailProps {
  connector: ConnectorResponse;
  onBack: () => void;
  onUpdated: (connector: ConnectorResponse) => void;
  onDeleted: (connectorId: string) => void;
}

export function ConnectorDetail({
  connector,
  onBack,
  onUpdated,
  onDeleted,
}: ConnectorDetailProps) {
  const [editing, setEditing] = useState(false);
  const [name, setName] = useState(connector.name);
  const [sourceConfig, setSourceConfig] = useState(connector.sourceConfig ?? '');
  const [scheduleCron, setScheduleCron] = useState(connector.scheduleCron ?? '');
  const [fieldMapping, setFieldMapping] = useState<FieldMappingConfig | null>(
    connector.fieldMapping,
  );
  const [saving, setSaving] = useState(false);

  const [testResult, setTestResult] = useState<TestConnectionResponse | null>(null);
  const [testing, setTesting] = useState(false);

  const [syncRuns, setSyncRuns] = useState<SyncRunSummary[]>([]);
  const [syncLoading, setSyncLoading] = useState(true);
  const [syncing, setSyncing] = useState(false);

  const [actionError, setActionError] = useState<string | null>(null);
  const [togglingStatus, setTogglingStatus] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);

  // Preview state (P3-027).
  const [previewing, setPreviewing] = useState(false);
  const [previewRecords, setPreviewRecords] = useState<PreviewRecord[] | null>(null);
  const [previewErrors, setPreviewErrors] = useState<string[]>([]);
  const [validationResult, setValidationResult] = useState<ConnectorValidationResult | null>(null);

  // Retrieval test state (P3-027).
  const [retrievalQuery, setRetrievalQuery] = useState('');
  const [retrievalTesting, setRetrievalTesting] = useState(false);
  const [retrievalChunks, setRetrievalChunks] = useState<PreviewRetrievalChunk[] | null>(null);
  const [retrievalTotal, setRetrievalTotal] = useState(0);
  const [retrievalMessage, setRetrievalMessage] = useState<string | null>(null);

  const loadSyncRuns = useCallback(async () => {
    setSyncLoading(true);
    try {
      const result = await api.listSyncRuns(connector.id);
      setSyncRuns(result.syncRuns);
    } catch (err) {
      logger.warn('[ConnectorDetail] Failed to load sync runs:', err);
      setSyncRuns([]);
    } finally {
      setSyncLoading(false);
    }
  }, [connector.id]);

  useEffect(() => {
    loadSyncRuns();
  }, [loadSyncRuns]);

  async function handleSave() {
    setSaving(true);
    setActionError(null);
    try {
      const req: UpdateConnectorRequest = {};
      if (name !== connector.name) req.name = name;
      if (sourceConfig !== (connector.sourceConfig ?? ''))
        req.sourceConfig = sourceConfig || undefined;
      if (scheduleCron !== (connector.scheduleCron ?? ''))
        req.scheduleCron = scheduleCron || undefined;
      if (fieldMapping !== connector.fieldMapping) req.fieldMapping = fieldMapping ?? undefined;

      const updated = await api.updateConnector(connector.id, req);
      onUpdated(updated);
      setEditing(false);
    } catch (e) {
      setActionError(e instanceof Error ? e.message : 'Failed to save');
    } finally {
      setSaving(false);
    }
  }

  async function handleTest() {
    setTesting(true);
    setTestResult(null);
    setActionError(null);
    try {
      const result = await api.testConnection(connector.id);
      setTestResult(result);
    } catch (e) {
      setActionError(e instanceof Error ? e.message : 'Test failed');
    } finally {
      setTesting(false);
    }
  }

  async function handleToggleStatus() {
    setTogglingStatus(true);
    setActionError(null);
    try {
      const updated =
        connector.status === ConnectorStatuses.Enabled
          ? await api.disableConnector(connector.id)
          : await api.enableConnector(connector.id);
      onUpdated(updated);
    } catch (e) {
      setActionError(e instanceof Error ? e.message : 'Failed to toggle status');
    } finally {
      setTogglingStatus(false);
    }
  }

  async function handleSyncNow(isBackfill: boolean) {
    setSyncing(true);
    setActionError(null);
    try {
      await api.syncNow(connector.id, { isBackfill });
      await loadSyncRuns();
    } catch (e) {
      setActionError(e instanceof Error ? e.message : 'Sync trigger failed');
    } finally {
      setSyncing(false);
    }
  }

  async function handlePreview() {
    setPreviewing(true);
    setPreviewRecords(null);
    setPreviewErrors([]);
    setValidationResult(null);
    setActionError(null);
    try {
      // Run preview and validate-mapping in parallel.
      const [previewResult, validation] = await Promise.all([
        api.previewConnector(connector.id, { sampleSize: 5 }),
        connector.fieldMapping
          ? api.validateMapping(connector.id, connector.fieldMapping)
          : Promise.resolve(null),
      ]);
      setPreviewRecords(previewResult.records);
      setPreviewErrors(previewResult.validationErrors);
      setValidationResult(validation);
    } catch (e) {
      setActionError(e instanceof Error ? e.message : 'Preview failed');
    } finally {
      setPreviewing(false);
    }
  }

  async function handleRetrievalTest() {
    if (!retrievalQuery.trim()) return;
    setRetrievalTesting(true);
    setRetrievalChunks(null);
    setRetrievalMessage(null);
    setActionError(null);
    try {
      const result = await api.previewRetrieval(connector.id, {
        query: retrievalQuery.trim(),
        maxResults: 5,
      });
      setRetrievalChunks(result.chunks);
      setRetrievalTotal(result.totalChunksForConnector);
      setRetrievalMessage(result.message);
    } catch (e) {
      setActionError(e instanceof Error ? e.message : 'Retrieval test failed');
    } finally {
      setRetrievalTesting(false);
    }
  }

  async function handleDelete() {
    setActionError(null);
    try {
      await api.deleteConnector(connector.id);
      onDeleted(connector.id);
    } catch (e) {
      setActionError(e instanceof Error ? e.message : 'Delete failed');
    }
  }

  return (
    <div className="connector-detail" data-testid="connector-detail">
      <div className="detail-header">
        <button className="btn btn-sm" onClick={onBack} data-testid="back-btn" aria-label="Back to connector list">
          &larr; Back
        </button>
        <h2>{connector.name}</h2>
        <span className={`connector-status ${connector.status === ConnectorStatuses.Enabled ? 'status-enabled' : 'status-disabled'}`}>
          {connector.status}
        </span>
      </div>

      {actionError && (
        <div className="error-banner" role="alert" data-testid="detail-error">
          {actionError}
        </div>
      )}

      <div className="detail-actions">
        <button
          className="btn"
          onClick={handleTest}
          disabled={testing}
          data-testid="test-connection-btn"
          aria-label={testing ? 'Testing connection' : 'Test connection'}
        >
          {testing ? 'Testing...' : 'Test Connection'}
        </button>
        <button
          className="btn"
          onClick={handleToggleStatus}
          disabled={togglingStatus}
          data-testid="toggle-status-btn"
          aria-label={togglingStatus ? 'Updating connector status' : connector.status === ConnectorStatuses.Enabled ? 'Disable connector' : 'Enable connector'}
        >
          {togglingStatus
            ? 'Updating...'
            : connector.status === ConnectorStatuses.Enabled
              ? 'Disable'
              : 'Enable'}
        </button>
        <button
          className="btn btn-primary"
          onClick={() => handleSyncNow(false)}
          disabled={syncing || connector.status === ConnectorStatuses.Disabled}
          data-testid="sync-now-btn"
          aria-label={syncing ? 'Syncing connector' : 'Sync connector now'}
        >
          {syncing ? 'Syncing...' : 'Sync Now'}
        </button>
        <button
          className="btn"
          onClick={() => handleSyncNow(true)}
          disabled={syncing || connector.status === ConnectorStatuses.Disabled}
          data-testid="backfill-btn"
          aria-label="Backfill connector data"
        >
          Backfill
        </button>
        <button
          className="btn"
          onClick={handlePreview}
          disabled={previewing}
          data-testid="preview-btn"
          aria-label={previewing ? 'Loading preview' : 'Preview connector data'}
        >
          {previewing ? 'Previewing...' : 'Preview'}
        </button>
        {!editing && (
          <button
            className="btn"
            onClick={() => setEditing(true)}
            data-testid="edit-btn"
            aria-label="Edit connector configuration"
          >
            Edit
          </button>
        )}
        {!confirmDelete ? (
          <button
            className="btn btn-danger-outline"
            onClick={() => setConfirmDelete(true)}
            data-testid="delete-btn"
            aria-label="Delete connector"
          >
            Delete
          </button>
        ) : (
          <span className="confirm-delete">
            <span>Are you sure?</span>
            <button className="btn btn-sm btn-danger" onClick={handleDelete} data-testid="confirm-delete-btn" aria-label="Confirm delete connector">
              Yes, Delete
            </button>
            <button className="btn btn-sm" onClick={() => setConfirmDelete(false)} aria-label="Cancel delete">
              Cancel
            </button>
          </span>
        )}
      </div>

      {testResult && (
        <div
          className={`test-result ${testResult.success ? 'test-success' : 'test-failure'}`}
          data-testid="test-result"
        >
          <strong>{testResult.success ? 'Connection successful' : 'Connection failed'}</strong>
          <p>{testResult.message}</p>
          {testResult.diagnosticDetail && (
            <pre className="test-diagnostic">{testResult.diagnosticDetail}</pre>
          )}
        </div>
      )}

      <div className="detail-info">
        <div className="info-grid">
          <div className="info-item">
            <span className="info-label">Type</span>
            <span className="info-value">{connector.connectorType}</span>
          </div>
          <div className="info-item">
            <span className="info-label">Auth</span>
            <span className="info-value">{connector.authType}</span>
          </div>
          <div className="info-item">
            <span className="info-label">Secret</span>
            <span className="info-value">{connector.hasSecret ? 'Configured' : 'Not set'}</span>
          </div>
          <div className="info-item">
            <span className="info-label">Created</span>
            <span className="info-value">{new Date(connector.createdAt).toLocaleString()}</span>
          </div>
        </div>

        {editing ? (
          <div className="edit-section" data-testid="edit-section">
            <div className="draft-field">
              <label className="draft-field-label">Name</label>
              <input
                type="text"
                value={name}
                onChange={(e) => setName(e.target.value)}
                data-testid="edit-name"
                aria-label="Connector name"
              />
            </div>
            <div className="draft-field">
              <label className="draft-field-label">Source Configuration</label>
              <SourceConfigEditor
                connectorType={connector.connectorType}
                value={sourceConfig}
                onChange={setSourceConfig}
              />
            </div>
            <div className="draft-field">
              <label className="draft-field-label">Schedule (Cron)</label>
              <input
                type="text"
                value={scheduleCron}
                onChange={(e) => setScheduleCron(e.target.value)}
                data-testid="edit-schedule"
                aria-label="Schedule cron expression"
              />
              <span className="field-hint">Sync runs on this schedule.</span>
            </div>

            <FieldMappingEditor mapping={fieldMapping} onChange={setFieldMapping} />

            <div className="edit-actions">
              <button
                className="btn btn-primary"
                onClick={handleSave}
                disabled={saving}
                data-testid="save-btn"
                aria-label={saving ? 'Saving connector changes' : 'Save connector changes'}
              >
                {saving ? 'Saving...' : 'Save Changes'}
              </button>
              <button className="btn" onClick={() => setEditing(false)} disabled={saving} aria-label="Cancel editing connector">
                Cancel
              </button>
            </div>
          </div>
        ) : (
          <>
            {connector.sourceConfig && (
              <div className="detail-section">
                <h4>Source Configuration</h4>
                <SourceConfigEditor
                  connectorType={connector.connectorType}
                  value={connector.sourceConfig}
                  onChange={() => {}}
                  readOnly
                />
              </div>
            )}
            {connector.scheduleCron && (
              <div className="detail-section">
                <h4>Schedule</h4>
                <p>{connector.scheduleCron}</p>
              </div>
            )}
            <FieldMappingEditor mapping={connector.fieldMapping} onChange={() => {}} readOnly />
          </>
        )}
      </div>

      <SyncRunHistory syncRuns={syncRuns} loading={syncLoading} />

      {/* Missing-field analysis (P3-027) */}
      {validationResult?.missingFieldAnalysis && (
        <div className="detail-section" data-testid="missing-field-analysis">
          <h4>Field Coverage</h4>
          {validationResult.missingFieldAnalysis.missingRequiredFields.length > 0 && (
            <div className="error-banner" role="alert" data-testid="missing-required-warning">
              Missing required fields: {validationResult.missingFieldAnalysis.missingRequiredFields.join(', ')}
            </div>
          )}
          <table className="data-table" aria-label="Field coverage analysis">
            <thead>
              <tr>
                <th>Field</th>
                <th>Required</th>
                <th>Mapped</th>
              </tr>
            </thead>
            <tbody>
              {validationResult.missingFieldAnalysis.fieldCoverage.map((f) => (
                <tr key={f.fieldName} className={f.isRequired && !f.isMapped ? 'missing-required' : ''}>
                  <td>{f.fieldName}</td>
                  <td>{f.isRequired ? 'Yes' : 'No'}</td>
                  <td>{f.isMapped ? 'Yes' : 'No'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {/* Preview records (P3-027) */}
      {previewRecords !== null && (
        <div className="detail-section" data-testid="preview-records">
          <h4>Preview Records ({previewRecords.length})</h4>
          {previewErrors.length > 0 && (
            <div className="error-banner" role="alert" data-testid="preview-errors">
              {previewErrors.map((err) => (
                <div key={err}>{err}</div>
              ))}
            </div>
          )}
          {previewRecords.length === 0 ? (
            <p>No sample records returned. Check source configuration and credentials.</p>
          ) : (
            <table className="data-table" aria-label="Preview records">
              <thead>
                <tr>
                  <th>Title</th>
                  <th>Source Type</th>
                  <th>Product Area</th>
                  <th>Content</th>
                </tr>
              </thead>
              <tbody>
                {previewRecords.map((r, i) => (
                  <tr key={`${r.title}-${r.sourceType}-${i}`}>
                    <td>{r.title || <span className="missing-field">missing</span>}</td>
                    <td>{r.sourceType || <span className="missing-field">missing</span>}</td>
                    <td>{r.productArea ?? '—'}</td>
                    <td className="preview-content">
                      {r.textContent
                        ? r.textContent.length > 200
                          ? r.textContent.slice(0, 200) + '...'
                          : r.textContent
                        : <span className="missing-field">missing</span>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      )}

      {/* Retrieval test (P3-027) */}
      <div className="detail-section" data-testid="retrieval-test">
        <h4>Retrieval Test</h4>
        <div className="retrieval-test-input">
          <input
            type="text"
            placeholder="Enter a test query..."
            value={retrievalQuery}
            onChange={(e) => setRetrievalQuery(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && handleRetrievalTest()}
            data-testid="retrieval-query-input"
            aria-label="Retrieval test query"
          />
          <button
            className="btn btn-primary"
            onClick={handleRetrievalTest}
            disabled={retrievalTesting || !retrievalQuery.trim()}
            data-testid="retrieval-test-btn"
            aria-label={retrievalTesting ? 'Searching retrieval results' : 'Test retrieval query'}
          >
            {retrievalTesting ? 'Searching...' : 'Test Retrieval'}
          </button>
        </div>

        {retrievalMessage && (
          <p className="retrieval-message" data-testid="retrieval-message">{retrievalMessage}</p>
        )}

        {retrievalChunks !== null && (
          <div data-testid="retrieval-results">
            <p className="retrieval-summary">
              {retrievalChunks.length} results from {retrievalTotal} total chunks
            </p>
            {retrievalChunks.length > 0 && (
              <table className="data-table" aria-label="Retrieval test results">
                <thead>
                  <tr>
                    <th>Title</th>
                    <th>Source Type</th>
                    <th>Product Area</th>
                    <th>Chunk Text</th>
                    <th>Updated</th>
                  </tr>
                </thead>
                <tbody>
                  {retrievalChunks.map((c) => (
                    <tr key={c.chunkId}>
                      <td>{c.title}</td>
                      <td>{c.sourceType}</td>
                      <td>{c.productArea ?? '—'}</td>
                      <td className="preview-content">{c.chunkText}</td>
                      <td>{new Date(c.updatedAt).toLocaleDateString()}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>
        )}
      </div>
    </div>
  );
}
