import { useCallback, useEffect, useState } from 'react';
import type {
  ConnectorResponse,
  FieldMappingConfig,
  SyncRunSummary,
  TestConnectionResponse,
  UpdateConnectorRequest,
} from '../api/types';
import * as api from '../api/client';
import { FieldMappingEditor } from './FieldMappingEditor';
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

  const loadSyncRuns = useCallback(async () => {
    setSyncLoading(true);
    try {
      const result = await api.listSyncRuns(connector.id);
      setSyncRuns(result.syncRuns);
    } catch {
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
        connector.status === 'Enabled'
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
        <button className="btn btn-sm" onClick={onBack} data-testid="back-btn">
          &larr; Back
        </button>
        <h2>{connector.name}</h2>
        <span className={`connector-status ${connector.status === 'Enabled' ? 'status-enabled' : 'status-disabled'}`}>
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
        >
          {testing ? 'Testing...' : 'Test Connection'}
        </button>
        <button
          className="btn"
          onClick={handleToggleStatus}
          disabled={togglingStatus}
          data-testid="toggle-status-btn"
        >
          {togglingStatus
            ? 'Updating...'
            : connector.status === 'Enabled'
              ? 'Disable'
              : 'Enable'}
        </button>
        <button
          className="btn btn-primary"
          onClick={() => handleSyncNow(false)}
          disabled={syncing || connector.status === 'Disabled'}
          data-testid="sync-now-btn"
        >
          {syncing ? 'Syncing...' : 'Sync Now'}
        </button>
        <button
          className="btn"
          onClick={() => handleSyncNow(true)}
          disabled={syncing || connector.status === 'Disabled'}
          data-testid="backfill-btn"
        >
          Backfill
        </button>
        {!editing && (
          <button
            className="btn"
            onClick={() => setEditing(true)}
            data-testid="edit-btn"
          >
            Edit
          </button>
        )}
        {!confirmDelete ? (
          <button
            className="btn btn-danger-outline"
            onClick={() => setConfirmDelete(true)}
            data-testid="delete-btn"
          >
            Delete
          </button>
        ) : (
          <span className="confirm-delete">
            <span>Are you sure?</span>
            <button className="btn btn-sm btn-danger" onClick={handleDelete} data-testid="confirm-delete-btn">
              Yes, Delete
            </button>
            <button className="btn btn-sm" onClick={() => setConfirmDelete(false)}>
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
              />
            </div>
            <div className="draft-field">
              <label className="draft-field-label">Source Config (JSON)</label>
              <textarea
                value={sourceConfig}
                onChange={(e) => setSourceConfig(e.target.value)}
                rows={5}
                data-testid="edit-source-config"
              />
            </div>
            <div className="draft-field">
              <label className="draft-field-label">Schedule (Cron)</label>
              <input
                type="text"
                value={scheduleCron}
                onChange={(e) => setScheduleCron(e.target.value)}
                data-testid="edit-schedule"
              />
              <span className="field-hint">Informational only in Phase 1.</span>
            </div>

            <FieldMappingEditor mapping={fieldMapping} onChange={setFieldMapping} />

            <div className="edit-actions">
              <button
                className="btn btn-primary"
                onClick={handleSave}
                disabled={saving}
                data-testid="save-btn"
              >
                {saving ? 'Saving...' : 'Save Changes'}
              </button>
              <button className="btn" onClick={() => setEditing(false)} disabled={saving}>
                Cancel
              </button>
            </div>
          </div>
        ) : (
          <>
            {connector.sourceConfig && (
              <div className="detail-section">
                <h4>Source Configuration</h4>
                <pre className="source-config-pre" data-testid="source-config-display">
                  {connector.sourceConfig}
                </pre>
              </div>
            )}
            {connector.scheduleCron && (
              <div className="detail-section">
                <h4>Schedule</h4>
                <p>{connector.scheduleCron} <span className="field-hint">(informational)</span></p>
              </div>
            )}
            <FieldMappingEditor mapping={connector.fieldMapping} onChange={() => {}} readOnly />
          </>
        )}
      </div>

      <SyncRunHistory syncRuns={syncRuns} loading={syncLoading} />
    </div>
  );
}
